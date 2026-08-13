using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Foundation;
using Windows.Media.Control;
using Newtonsoft.Json.Linq;

namespace PixelLyric8BitFix
{
    public partial class MainWindow : Window
    {
        private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
        private GlobalSystemMediaTransportControlsSession? _currentSession;
        private readonly DispatcherTimer _smoothTimer;
        private readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };

        // ── 播放进度：锚点 + 插值 ──────────────────────────────────────────
        // 不再每 50ms 调 WinRT API，而是只在系统真的广播了新状态时打一个锚点，
        // 平时用纯本地时间数学去插值，既省资源又比“每次读一次系统缓存值”更准。
        private TimeSpan _anchorPosition = TimeSpan.Zero;
        private DateTimeOffset _anchorTime = DateTimeOffset.Now;
        private TimeSpan _totalDuration = TimeSpan.Zero;
        private bool _isPlaying = false;
        private double _playbackRate = 1.0;

        // ── 歌词：有序列表 + 游标，避免每 50ms 全表扫描 ─────────────────────
        private readonly object _lyricLock = new();
        private List<(int TimeMs, string Text)> _lyricLines = new();
        private int _lyricCursor = -1;

        private string _lastTrackId = "";
        private CancellationTokenSource? _lyricFetchCts;

        // 事件处理器要保留引用，换 session 时才能正确 -= 掉，防止叠加订阅
        private TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs>? _mediaPropsHandler;
        private TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs>? _playbackHandler;
        private TypedEventHandler<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs>? _timelineHandler;

        private readonly AppSettings _settings;

        // ── MC 皮肤：Steve 走路循环状态 ───────────────────────────────────
        private BitmapSource? _steveFrame1;
        private BitmapSource? _steveFrame2;
        private double _steveLastX;
        private int _steveLegTickCounter;

        // App 用 ShutdownMode="OnExplicitShutdown"：这个窗口被关掉时，
        // 如果是"跳去设置页"这种正常流程就不退出程序，否则（双击 / 右键退出 / Alt+F4）才真退出。
        private bool _navigatingToSettings = false;

        private UpdateInfo? _updateInfo;
        private bool _updateInProgress;

        public MainWindow() : this(AppSettings.Load()) { }

        public MainWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings ?? new AppSettings();

            // 启动设置页选好的：尺寸预设、皮肤、显示模式
            var (w, h) = _settings.GetWindowSize();
            Width = w;
            Height = h;

            // 选完预设后直接锁死大小，不允许拖拽缩放，避免误拉成全屏
            ResizeMode = ResizeMode.NoResize;
            WindowResizeGrip.Visibility = Visibility.Collapsed;

            ApplySkin(_settings.Skin);
            ApplyDisplayMode(_settings.DisplayMode);

            Loaded += MainWindow_Loaded;
            Closed += (s, e) => { if (!_navigatingToSettings) Application.Current.Shutdown(); };

            this.MouseDown += (s, e) => { if (e.ChangedButton == MouseButton.Left) this.DragMove(); };
            this.MouseDoubleClick += (s, e) => Application.Current.Shutdown();

            // 右键菜单：改皮肤 / 退出（因为窗口本身没有标题栏，双击关闭比较容易误触，多给一个入口）
            var menu = new ContextMenu();
            var itemSkin = new MenuItem { Header = "⚙ 更改皮肤 / 设置" };
            itemSkin.Click += (s, e) => OpenSettingsAndClose();
            var itemExit = new MenuItem { Header = "✕ 退出" };
            itemExit.Click += (s, e) => Application.Current.Shutdown();
            menu.Items.Add(itemSkin);
            menu.Items.Add(new Separator());
            menu.Items.Add(itemExit);
            this.ContextMenu = menu;

            // 50ms 只做本地插值计算 + UI 刷新，不再有系统调用，非常轻量
            _smoothTimer = new DispatcherTimer(DispatcherPriority.Render);
            _smoothTimer.Interval = TimeSpan.FromMilliseconds(50);
            _smoothTimer.Tick += SmoothTimer_Tick;

            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        }

        // 齿轮按钮点击：e.Handled=true 防止事件冒泡到 Window 的 MouseDown 触发 DragMove（否则点击会被当成拖拽吞掉）
        private void BtnSettings_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            OpenSettingsAndClose();
        }

        // 从主播放器窗口"返回"设置页：先开新窗口，再关自己，中间不会出现"零窗口"的瞬间
        private void OpenSettingsAndClose()
        {
            _navigatingToSettings = true;

            var settingsWindow = new SettingsWindow();
            Application.Current.MainWindow = settingsWindow;
            settingsWindow.Show();

            Close();
        }

        // 启动几秒后在后台悄悄查一次有没有新版本，查不到 / 没网络都不影响正常使用
        private async Task CheckForUpdateInBackgroundAsync()
        {
            var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            var info = await UpdateChecker.CheckForUpdateAsync(currentVersion, _httpClient);
            if (info == null) return;

            Dispatcher.Invoke(() =>
            {
                _updateInfo = info;
                TxtUpdateBadge.Text = $"🎉 新版本 v{info.Version}";
                UpdateBadge.Visibility = Visibility.Visible;
            });
        }

        // 点新版本徽标：一键下载安装包 + 静默装上 + 自动重启进新版本；
        // 找不到安装包直链时退回成打开浏览器手动下载，不会卡住什么都不做
        private async void UpdateBadge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (_updateInProgress || _updateInfo == null) return;

            if (string.IsNullOrEmpty(_updateInfo.InstallerDownloadUrl))
            {
                try { Process.Start(new ProcessStartInfo(_updateInfo.ReleaseUrl) { UseShellExecute = true }); } catch { }
                return;
            }

            _updateInProgress = true;
            TxtUpdateBadge.Text = "⬇ 下载中... 0%";

            try
            {
                var progress = new Progress<double>(p =>
                {
                    TxtUpdateBadge.Text = $"⬇ 下载中... {(int)(p * 100)}%";
                });

                string installerPath = await UpdateChecker.DownloadInstallerAsync(
                    _updateInfo.InstallerDownloadUrl, _httpClient, progress, CancellationToken.None);

                TxtUpdateBadge.Text = "✅ 正在安装...";
                UpdateChecker.LaunchInstallerAndExit(installerPath);
            }
            catch
            {
                _updateInProgress = false;
                TxtUpdateBadge.Text = "⚠️ 下载失败，点击重试";
            }
        }

        // 皮肤：先把 6 套背景层 / 装饰层的显隐都摆对，再套上文字调色板
        private void ApplySkin(PlayerSkin skin)
        {
            MinecraftSkinBg.Visibility = Visibility.Collapsed;
            SimpleSkinBg.Visibility = Visibility.Collapsed;
            CrtSkinBg.Visibility = Visibility.Collapsed;
            CyberpunkSkinBg.Visibility = Visibility.Collapsed;
            VinylSkinBg.Visibility = Visibility.Collapsed;
            GlassSkinBg.Visibility = Visibility.Collapsed;
            LofiSkinBg.Visibility = Visibility.Collapsed;
            AuroraSkinBg.Visibility = Visibility.Collapsed;
            RainSkinBg.Visibility = Visibility.Collapsed;
            StarrySkinBg.Visibility = Visibility.Collapsed;
            CampfireSkinBg.Visibility = Visibility.Collapsed;
            SakuraSkinBg.Visibility = Visibility.Collapsed;
            CassetteSkinBg.Visibility = Visibility.Collapsed;
            MinecraftDecorCanvas.Visibility = Visibility.Collapsed;
            VinylDecorCanvas.Visibility = Visibility.Collapsed;
            LofiDecorCanvas.Visibility = Visibility.Collapsed;
            CampfireDecorCanvas.Visibility = Visibility.Collapsed;
            CassetteDecorCanvas.Visibility = Visibility.Collapsed;
            AuroraSnowOverlay.Visibility = Visibility.Collapsed;
            RainOverlay.Visibility = Visibility.Collapsed;
            StarryOverlay.Visibility = Visibility.Collapsed;
            SakuraOverlay.Visibility = Visibility.Collapsed;
            ScanlineOverlay.Visibility = Visibility.Collapsed;

            // 只有 Minecraft / 黑胶 / Lofi / 篝火 / 磁带机 需要顶部那条装饰行，其余皮肤更简洁，不留空行
            RowDecor.Height = (skin == PlayerSkin.Minecraft || skin == PlayerSkin.Vinyl ||
                                skin == PlayerSkin.Lofi || skin == PlayerSkin.Campfire ||
                                skin == PlayerSkin.Cassette)
                ? new GridLength(50) : new GridLength(0);

            switch (skin)
            {
                case PlayerSkin.Minecraft:
                    MinecraftSkinBg.Visibility = Visibility.Visible;
                    MinecraftDecorCanvas.Visibility = Visibility.Visible;
                    ImgTree.Source = PixelArt.CreateTree();
                    DirtBrush.ImageSource = PixelArt.CreateDirtTile();
                    _steveFrame1 = PixelArt.CreateSteveFrame1();
                    _steveFrame2 = PixelArt.CreateSteveFrame2();
                    ImgSteve.Source = _steveFrame1;
                    break;

                case PlayerSkin.Crt:
                    CrtSkinBg.Visibility = Visibility.Visible;
                    ScanlineOverlay.Visibility = Visibility.Visible;
                    ScanlineBrush.ImageSource = PixelArt.CreateScanlineTile();
                    break;

                case PlayerSkin.Cyberpunk:
                    CyberpunkSkinBg.Visibility = Visibility.Visible;
                    break;

                case PlayerSkin.Vinyl:
                    VinylSkinBg.Visibility = Visibility.Visible;
                    VinylDecorCanvas.Visibility = Visibility.Visible;
                    break;

                case PlayerSkin.Glass:
                    GlassSkinBg.Visibility = Visibility.Visible;
                    break;

                case PlayerSkin.Lofi:
                    LofiSkinBg.Visibility = Visibility.Visible;
                    LofiDecorCanvas.Visibility = Visibility.Visible;
                    ImgCoffeeCup.Source = PixelArt.CreateCoffeeCup();
                    break;

                case PlayerSkin.Aurora:
                    AuroraSkinBg.Visibility = Visibility.Visible;
                    AuroraSnowOverlay.Visibility = Visibility.Visible;
                    break;

                case PlayerSkin.Rain:
                    RainSkinBg.Visibility = Visibility.Visible;
                    RainOverlay.Visibility = Visibility.Visible;
                    break;

                case PlayerSkin.Starry:
                    StarrySkinBg.Visibility = Visibility.Visible;
                    StarryOverlay.Visibility = Visibility.Visible;
                    break;

                case PlayerSkin.Campfire:
                    CampfireSkinBg.Visibility = Visibility.Visible;
                    CampfireDecorCanvas.Visibility = Visibility.Visible;
                    ImgCampfire.Source = PixelArt.CreateCampfire();
                    break;

                case PlayerSkin.Sakura:
                    SakuraSkinBg.Visibility = Visibility.Visible;
                    SakuraOverlay.Visibility = Visibility.Visible;
                    break;

                case PlayerSkin.Cassette:
                    CassetteSkinBg.Visibility = Visibility.Visible;
                    CassetteDecorCanvas.Visibility = Visibility.Visible;
                    break;

                case PlayerSkin.Simple:
                default:
                    SimpleSkinBg.Visibility = Visibility.Visible;
                    break;
            }

            ApplySkinPalette(skin);
        }

        // 每套皮肤自己的字体 / 文字颜色 / 歌词框配色 / 发光效果
        private void ApplySkinPalette(PlayerSkin skin)
        {
            (FontFamily font, Color title, Color artist, Color accent, Color lyric, Color glow, double glowBlur, Brush lyricBoxBg, Brush lyricBoxBorder) p = skin switch
            {
                PlayerSkin.Crt => (
                    new FontFamily("Consolas"),
                    Color.FromRgb(0x33, 0xFF, 0x66), Color.FromRgb(0x22, 0xCC, 0x55), Color.FromRgb(0x33, 0xFF, 0x66),
                    Color.FromRgb(0x66, 0xFF, 0xAA), Color.FromRgb(0x33, 0xFF, 0x66), 6,
                    new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0)), new SolidColorBrush(Color.FromRgb(0x1A, 0x66, 0x1A))),

                PlayerSkin.Cyberpunk => (
                    new FontFamily("Segoe UI Semibold"),
                    Color.FromRgb(0xFF, 0x2E, 0xD1), Color.FromRgb(0x00, 0xF0, 0xFF), Color.FromRgb(0x00, 0xF0, 0xFF),
                    Color.FromRgb(0xFF, 0x2E, 0xD1), Color.FromRgb(0x00, 0xF0, 0xFF), 10,
                    new SolidColorBrush(Color.FromArgb(0xB3, 0x12, 0x00, 0x22)), new SolidColorBrush(Color.FromRgb(0xFF, 0x2E, 0xD1))),

                PlayerSkin.Vinyl => (
                    new FontFamily("Georgia"),
                    Color.FromRgb(0xF1, 0xE3, 0xC6), Color.FromRgb(0xC9, 0xA2, 0x27), Color.FromRgb(0xC9, 0xA2, 0x27),
                    Color.FromRgb(0xF1, 0xE3, 0xC6), Colors.Black, 2,
                    new SolidColorBrush(Color.FromArgb(0xCC, 0x1A, 0x12, 0x0C)), new SolidColorBrush(Color.FromRgb(0xC9, 0xA2, 0x27))),

                PlayerSkin.Glass => (
                    new FontFamily("Segoe UI Light"),
                    Colors.White, Color.FromRgb(0xEA, 0xEA, 0xEA), Colors.White,
                    Colors.White, Colors.Black, 4,
                    new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)), new SolidColorBrush(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF))),

                PlayerSkin.Simple => (
                    new FontFamily("Segoe UI"),
                    Colors.White, Color.FromRgb(0xAA, 0xAA, 0xAA), Color.FromRgb(0x8A, 0xB4, 0xF8),
                    Colors.White, Colors.Transparent, 0,
                    new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26)), new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A))),

                PlayerSkin.Lofi => (
                    new FontFamily("Georgia"),
                    Color.FromRgb(0xE8, 0xD9, 0xC0), Color.FromRgb(0xB8, 0x96, 0x6E), Color.FromRgb(0xC8, 0x96, 0x66),
                    Color.FromRgb(0xE8, 0xD9, 0xC0), Colors.Black, 2,
                    new SolidColorBrush(Color.FromArgb(0xCC, 0x1A, 0x12, 0x0C)), new SolidColorBrush(Color.FromRgb(0x8A, 0x6A, 0x4A))),

                PlayerSkin.Aurora => (
                    new FontFamily("Segoe UI Light"),
                    Color.FromRgb(0xE4, 0xF9, 0xF5), Color.FromRgb(0x9F, 0xD8, 0xCE), Color.FromRgb(0x4F, 0xD8, 0xC4),
                    Color.FromRgb(0xBF, 0xEF, 0xFF), Color.FromRgb(0x4F, 0xD8, 0xC4), 5,
                    new SolidColorBrush(Color.FromArgb(0xB3, 0x0B, 0x1E, 0x2D)), new SolidColorBrush(Color.FromRgb(0x4F, 0xD8, 0xC4))),

                PlayerSkin.Rain => (
                    new FontFamily("Segoe UI Light"),
                    Color.FromRgb(0xC9, 0xDA, 0xE3), Color.FromRgb(0x8A, 0xA3, 0xB0), Color.FromRgb(0xA8, 0xC5, 0xD6),
                    Color.FromRgb(0xA8, 0xC5, 0xD6), Color.FromRgb(0x4A, 0x65, 0x72), 3,
                    new SolidColorBrush(Color.FromArgb(0xCC, 0x0D, 0x13, 0x18)), new SolidColorBrush(Color.FromRgb(0x4A, 0x65, 0x72))),

                PlayerSkin.Starry => (
                    new FontFamily("Segoe UI Light"),
                    Colors.White, Color.FromRgb(0xA8, 0xB4, 0xE8), Color.FromRgb(0x6B, 0x7F, 0xD7),
                    Color.FromRgb(0xC9, 0xD6, 0xFF), Color.FromRgb(0x6B, 0x7F, 0xD7), 4,
                    new SolidColorBrush(Color.FromArgb(0xCC, 0x0A, 0x0E, 0x27)), new SolidColorBrush(Color.FromRgb(0x6B, 0x7F, 0xD7))),

                PlayerSkin.Campfire => (
                    new FontFamily("Georgia"),
                    Color.FromRgb(0xF5, 0xE0, 0xC8), Color.FromRgb(0xD9, 0x7B, 0x36), Color.FromRgb(0xF0, 0xA8, 0x68),
                    Color.FromRgb(0xF5, 0xE0, 0xC8), Color.FromRgb(0xD9, 0x7B, 0x36), 3,
                    new SolidColorBrush(Color.FromArgb(0xCC, 0x12, 0x18, 0x0F)), new SolidColorBrush(Color.FromRgb(0xD9, 0x7B, 0x36))),

                PlayerSkin.Sakura => (
                    new FontFamily("Segoe UI Light"),
                    Color.FromRgb(0xFB, 0xE4, 0xEE), Color.FromRgb(0xE8, 0xB8, 0xCE), Color.FromRgb(0xF7, 0xA8, 0xC4),
                    Color.FromRgb(0xFB, 0xE4, 0xEE), Color.FromRgb(0xF7, 0xA8, 0xC4), 3,
                    new SolidColorBrush(Color.FromArgb(0xCC, 0x24, 0x18, 0x26)), new SolidColorBrush(Color.FromRgb(0xF7, 0xA8, 0xC4))),

                // 磁带机是唯一浅色背景的皮肤，标题/艺人文字要用深色才看得清；
                // 歌词框保留深棕底 + 米色字，做成"磁带标签窗口"的观感，跟其他深色皮肤的歌词框风格保持一致
                PlayerSkin.Cassette => (
                    new FontFamily("Courier New"),
                    Color.FromRgb(0x3A, 0x2E, 0x22), Color.FromRgb(0x6B, 0x56, 0x42), Color.FromRgb(0xC1, 0x44, 0x0E),
                    Color.FromRgb(0xF0, 0xE6, 0xD2), Colors.Black, 0,
                    new SolidColorBrush(Color.FromArgb(0xE6, 0x3A, 0x2E, 0x22)), new SolidColorBrush(Color.FromRgb(0x8A, 0x6A, 0x4A))),

                _ => ( // Minecraft（默认）
                    new FontFamily("Lucida Console"),
                    Colors.White, Color.FromRgb(0xDD, 0xDD, 0xDD), Color.FromRgb(0x55, 0xFF, 0x55),
                    Color.FromRgb(0xFF, 0xFF, 0x55), Color.FromRgb(0x3F, 0x3F, 0x00), 0,
                    new SolidColorBrush(Color.FromArgb(0xD9, 0x11, 0x11, 0x11)), new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22))),
            };

            TxtSongTitle.FontFamily = p.font;
            TxtArtist.FontFamily = p.font;
            TxtTime.FontFamily = p.font;
            TxtDynamicLyric.FontFamily = p.font;

            TxtSongTitle.Foreground = new SolidColorBrush(p.title);
            TxtArtist.Foreground = new SolidColorBrush(p.artist);
            TxtTime.Foreground = new SolidColorBrush(p.artist);

            LyricProgressBar.Foreground = new SolidColorBrush(p.accent);
            LyricProgressBar.Effect = new DropShadowEffect { Color = p.accent, BlurRadius = 5, ShadowDepth = 0 };

            LyricBox.Background = p.lyricBoxBg;
            LyricBox.BorderBrush = p.lyricBoxBorder;

            TxtDynamicLyric.Foreground = new SolidColorBrush(p.lyric);
            TxtDynamicLyric.Effect = p.glowBlur > 0
                ? new DropShadowEffect { Color = p.glow, BlurRadius = p.glowBlur, ShadowDepth = 0, Opacity = 0.9 }
                : null;
        }

        // Steve 来回走：范围按窗口实际宽度算，不再是写死的 20~420（小尺寸会走出界，大尺寸又用不满）
        private void StartSteveWalking()
        {
            double rightBound = Math.Max(60, Width - 110); // 110 = 两侧 Canvas 内边距 + 预留给树的位置
            var duration = TimeSpan.FromSeconds(7);

            var xAnim = new DoubleAnimation(20, rightBound, duration) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            SteveTransform.BeginAnimation(TranslateTransform.XProperty, xAnim);

            var shadowAnim = new DoubleAnimation(20, rightBound, duration) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            ShadowTransform.BeginAnimation(TranslateTransform.XProperty, shadowAnim);

            _steveLastX = 20;
        }

        // 每 tick 检查一次：走路换腿（约 250ms 一帧）+ 按位移方向翻面朝向
        private void UpdateSteveWalkAnimation()
        {
            double currentX = SteveTransform.X;

            if (currentX > _steveLastX + 0.05) SteveFlip.ScaleX = 1;   // 在往右走，面朝右
            else if (currentX < _steveLastX - 0.05) SteveFlip.ScaleX = -1; // 在往左走，翻转面朝左
            _steveLastX = currentX;

            _steveLegTickCounter++;
            if (_steveLegTickCounter >= 5) // 5 × 50ms ≈ 250ms 切一次腿部姿势
            {
                _steveLegTickCounter = 0;
                ImgSteve.Source = ImgSteve.Source == _steveFrame1 ? _steveFrame2 : _steveFrame1;
            }
        }

        // 显示模式：极简只留歌词一行，标准把标题/进度条也带上
        private void ApplyDisplayMode(PlayerDisplayMode mode)
        {
            bool minimal = mode == PlayerDisplayMode.Minimal;

            TitleRow.Visibility = minimal ? Visibility.Collapsed : Visibility.Visible;
            ProgressRow.Visibility = minimal ? Visibility.Collapsed : Visibility.Visible;
            RowTitle.Height = minimal ? new GridLength(0) : GridLength.Auto;
            RowProgress.Height = minimal ? new GridLength(0) : GridLength.Auto;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // 每套皮肤都配一个属于自己气质的动态效果，不再只有 MC 会动
                string? ambientAnimationKey = _settings.Skin switch
                {
                    PlayerSkin.Minecraft => "MinecraftWorldAnimation",
                    PlayerSkin.Simple => "SimplePulseAnimation",
                    PlayerSkin.Crt => "CrtFlickerAnimation",
                    PlayerSkin.Cyberpunk => "CyberpunkGlowAnimation",
                    PlayerSkin.Vinyl => "VinylSpinAnimation",
                    PlayerSkin.Glass => "GlassCrackAnimation",
                    PlayerSkin.Lofi => "LofiSteamAnimation",
                    PlayerSkin.Aurora => "AuroraSkyAnimation",
                    PlayerSkin.Rain => "RainAnimation",
                    PlayerSkin.Starry => "StarryTwinkleAnimation",
                    PlayerSkin.Campfire => "CampfireAnimation",
                    PlayerSkin.Sakura => "SakuraAnimation",
                    PlayerSkin.Cassette => "CassetteSpinAnimation",
                    _ => null,
                };
                if (ambientAnimationKey != null)
                {
                    ((Storyboard)this.Resources[ambientAnimationKey]).Begin(this);
                }

                if (_settings.Skin == PlayerSkin.Minecraft)
                {
                    StartSteveWalking();
                }

                _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

                _sessionManager.CurrentSessionChanged += async (s, args) =>
                {
                    await BindSessionEventsAsync(_sessionManager?.GetCurrentSession());
                };

                await BindSessionEventsAsync(_sessionManager.GetCurrentSession());

                _smoothTimer.Start();

                _ = CheckForUpdateInBackgroundAsync();
            }
            catch (Exception ex)
            {
                TxtDynamicLyric.Text = $"INIT ERROR: {ex.Message.ToUpper()}";
            }
        }

        private async Task BindSessionEventsAsync(GlobalSystemMediaTransportControlsSession? session)
        {
            // 先解绑旧 session 的事件，防止切设备/切应用时回调越叠越多
            if (_currentSession != null)
            {
                if (_mediaPropsHandler != null) _currentSession.MediaPropertiesChanged -= _mediaPropsHandler;
                if (_playbackHandler != null) _currentSession.PlaybackInfoChanged -= _playbackHandler;
                if (_timelineHandler != null) _currentSession.TimelinePropertiesChanged -= _timelineHandler;
            }

            _currentSession = session;
            if (session == null) return;

            // 切歌事件
            _mediaPropsHandler = async (s, args) =>
            {
                var props = await s.TryGetMediaPropertiesAsync();
                await HandleTrackChangeAsync(props.Title, props.Artist);
            };
            // 播放/暂停/倍速/快进快退 —— 任何一个变化都重新打锚点
            _playbackHandler = (s, args) => RefreshAnchor(s);
            // 系统主动广播的时间轴更新（各播放器广播频率不同，但比我们瞎猜准）
            _timelineHandler = (s, args) => RefreshAnchor(s);

            session.MediaPropertiesChanged += _mediaPropsHandler;
            session.PlaybackInfoChanged += _playbackHandler;
            session.TimelinePropertiesChanged += _timelineHandler;

            var mediaProperties = await session.TryGetMediaPropertiesAsync();
            if (mediaProperties != null)
            {
                await HandleTrackChangeAsync(mediaProperties.Title, mediaProperties.Artist);
            }
            RefreshAnchor(session);
        }

        private async Task HandleTrackChangeAsync(string title, string artist)
        {
            if (string.IsNullOrEmpty(title)) return;
            string trackId = $"{title}_{artist}";
            if (_lastTrackId == trackId) return;
            _lastTrackId = trackId;

            // 1. 切歌一瞬间，UI 立刻响应，绝不等待网络
            Dispatcher.Invoke(() =>
            {
                TxtSongTitle.Text = title.ToUpper();
                TxtArtist.Text = $"BY {artist.ToUpper()}";
                TxtDynamicLyric.Text = "⛏️ MINING NEW TRACK...";
                TxtTime.Text = "[00:00 / 00:00]";
                LyricProgressBar.Value = 0;
            });
            lock (_lyricLock)
            {
                _lyricLines = new List<(int, string)>();
                _lyricCursor = -1;
            }

            // 2. 取消上一首还没返回的抓词请求，防止旧结果覆盖新歌，也少占带宽
            _lyricFetchCts?.Cancel();
            var cts = new CancellationTokenSource();
            _lyricFetchCts = cts;

            // 3. 立刻按新 session 状态刷新一次锚点（避免残留上一首的进度），
            //    顺便记下这首歌"官方"的真实时长——要放在抓词之前，因为抓词回来还要拿它校验版本对不对
            if (_currentSession != null) RefreshAnchor(_currentSession);
            TimeSpan expectedDuration = _totalDuration;

            // 4. 歌名数据清洗
            string cleanTitle = Regex.Replace(title, @"\s*[\(\[][^\]\)]*(feat|with|remix|version|prod)[^\]\)]*[\)\]]", "", RegexOptions.IgnoreCase).Trim();
            string cleanArtist = artist.Split(new[] { ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? artist;

            // 5. 核心：四个免费歌词源并发抓取，谁先给出有效结果就用谁，主线程完全不等待
            _ = Task.Run(() => FetchLyricsAnyEngineAsync(cleanTitle, cleanArtist, trackId, expectedDuration, cts.Token));
        }

        // 只在系统真正广播新状态时调用：记录一个“锚点”，其余时间靠本地插值
        private void RefreshAnchor(GlobalSystemMediaTransportControlsSession session)
        {
            if (session == null) return;
            try
            {
                var timeline = session.GetTimelineProperties();
                var playback = session.GetPlaybackInfo();

                var duration = timeline.EndTime - timeline.StartTime;

                Dispatcher.Invoke(() =>
                {
                    _anchorPosition = timeline.Position;
                    _anchorTime = DateTimeOffset.Now;
                    _totalDuration = duration > TimeSpan.Zero ? duration : TimeSpan.Zero;
                    _isPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    _playbackRate = (playback?.PlaybackRate is double r && r > 0) ? r : 1.0;
                });
            }
            catch { }
        }

        private void SmoothTimer_Tick(object? sender, EventArgs e)
        {
            // 纯本地数学插值，不再调用任何系统 API —— 这是这次改动最大的性能收益点
            TimeSpan position = _anchorPosition;
            if (_isPlaying)
            {
                var elapsedRealTime = DateTimeOffset.Now - _anchorTime;
                position += TimeSpan.FromTicks((long)(elapsedRealTime.Ticks * _playbackRate));
            }

            if (position > _totalDuration) position = _totalDuration;
            if (position < TimeSpan.Zero) position = TimeSpan.Zero;

            TxtTime.Text = $"[{position.Minutes:D2}:{position.Seconds:D2} / {_totalDuration.Minutes:D2}:{_totalDuration.Seconds:D2}]";
            LyricProgressBar.Maximum = _totalDuration.TotalMilliseconds > 0 ? _totalDuration.TotalMilliseconds : 100;
            LyricProgressBar.Value = position.TotalMilliseconds;

            UpdateLyricDisplay((int)position.TotalMilliseconds);

            if (_settings.Skin == PlayerSkin.Minecraft)
            {
                UpdateSteveWalkAnimation();
            }
        }

        // ── 多引擎并发抓词：LRCLIB / 网易云 / QQ音乐 / 酷狗，谁先成功用谁 ──────
        private async Task FetchLyricsAnyEngineAsync(string title, string artist, string trackId, TimeSpan expectedDuration, CancellationToken token)
        {
            var pending = new List<Task<string?>>
            {
                FetchFromLrcLibAsync(title, artist, token),
                FetchFromNeteaseAsync(title, artist, token),
                FetchFromQQMusicAsync(title, artist, token),
                FetchFromKugouAsync(title, artist, token),
            };

            while (pending.Count > 0)
            {
                var finished = await Task.WhenAny(pending);
                pending.Remove(finished);

                if (token.IsCancellationRequested) return; // 已经切歌了，这个结果作废

                string? lrc = null;
                try { lrc = await finished; } catch { /* 单引擎异常已在内部吞掉，这里兜底 */ }

                if (string.IsNullOrEmpty(lrc)) continue;

                // 网上同一首歌常常有好几个版本的 LRC（原版/加速版/Remix/电台剪辑），
                // 抓错版本的话时长对不上，歌词会越走越偏。拿系统报告的真实播放时长交叉校验一下，
                // 明显对不上就当这个引擎没抓到，换下一个，而不是硬塞一份会跑偏的歌词。
                if (!IsDurationPlausible(lrc, expectedDuration)) continue;

                if (token.IsCancellationRequested || trackId != _lastTrackId) return; // 双重保险
                ParseLrcText(lrc);
                return;
            }

            // 四个引擎全部没找到（或者找到的版本时长都对不上）
            if (!token.IsCancellationRequested && trackId == _lastTrackId)
            {
                Dispatcher.Invoke(() =>
                {
                    lock (_lyricLock)
                    {
                        if (_lyricLines.Count == 0) TxtDynamicLyric.Text = "NO LYRICS FOUND (4 ENGINES TRIED)";
                    }
                });
            }
        }

        // 用 LRC 里最后一句歌词的时间戳粗略估算"这份歌词是多长的版本"，
        // 跟系统报告的真实播放时长做个交叉验证。放宽到 75%，是为了不误伤那些
        // 最后一句歌词离结尾还有一段纯音乐尾奏的正常歌曲。
        private static bool IsDurationPlausible(string lrcContent, TimeSpan expectedDuration)
        {
            if (expectedDuration <= TimeSpan.Zero) return true; // 系统没报时长，没法校验，别拦

            var lastTimestamp = GetLastLyricTimestamp(lrcContent);
            if (lastTimestamp == null || lastTimestamp.Value <= TimeSpan.Zero) return true; // 解析不出来也别拦

            double ratio = lastTimestamp.Value.TotalSeconds / expectedDuration.TotalSeconds;
            return ratio >= 0.75;
        }

        private static readonly Regex LrcTimestampRegex = new(@"\[(?<min>\d+):(?<sec>\d+)[\.:](?<ms>\d+)\]", RegexOptions.Compiled);

        private static TimeSpan? GetLastLyricTimestamp(string lrcContent)
        {
            int maxMs = -1;
            foreach (Match m in LrcTimestampRegex.Matches(lrcContent))
            {
                int min = int.Parse(m.Groups["min"].Value);
                int sec = int.Parse(m.Groups["sec"].Value);
                string msStr = m.Groups["ms"].Value;
                if (msStr.Length == 1) msStr += "00";
                else if (msStr.Length == 2) msStr += "0";
                int ms = int.Parse(msStr.Substring(0, 3));

                int totalMs = (min * 60 + sec) * 1000 + ms;
                if (totalMs > maxMs) maxMs = totalMs;
            }
            return maxMs >= 0 ? TimeSpan.FromMilliseconds(maxMs) : null;
        }

        // 1) LRCLIB —— 完全开放、免费、无需 key，同步歌词质量高，优先级最高
        private async Task<string?> FetchFromLrcLibAsync(string title, string artist, CancellationToken token)
        {
            try
            {
                string url = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
                string json = await _httpClient.GetStringAsync(url, token);
                return JObject.Parse(json)["syncedLyrics"]?.ToString();
            }
            catch { return null; }
        }

        // 2) 网易云音乐（非官方接口）—— 中文歌曲命中率高
        private async Task<string?> FetchFromNeteaseAsync(string title, string artist, CancellationToken token)
        {
            try
            {
                string searchUrl = $"https://music.163.com/api/search/get/web?s={Uri.EscapeDataString(title + " " + artist)}&type=1&limit=1";
                string searchJson = await _httpClient.GetStringAsync(searchUrl, token);
                string? songId = JObject.Parse(searchJson)["result"]?["songs"]?[0]?["id"]?.ToString();
                if (string.IsNullOrEmpty(songId)) return null;

                string lyricUrl = $"https://music.163.com/api/song/lyric?os=pc&id={songId}&lv=-1&kv=-1&tv=-1";
                string lyricJson = await _httpClient.GetStringAsync(lyricUrl, token);
                return JObject.Parse(lyricJson)["lrc"]?["lyric"]?.ToString();
            }
            catch { return null; }
        }

        // 3) QQ音乐（非官方接口）—— 需要带 Referer 否则会被拒绝
        private async Task<string?> FetchFromQQMusicAsync(string title, string artist, CancellationToken token)
        {
            try
            {
                string keyword = Uri.EscapeDataString($"{title} {artist}");

                using var searchReq = new HttpRequestMessage(HttpMethod.Get,
                    $"https://c.y.qq.com/soso/fcgi-bin/client_search_cp?w={keyword}&format=json&p=1&n=1");
                searchReq.Headers.Referrer = new Uri("https://y.qq.com/");
                using var searchResp = await _httpClient.SendAsync(searchReq, token);
                string searchJson = await searchResp.Content.ReadAsStringAsync(token);
                string? songMid = JObject.Parse(searchJson)["data"]?["song"]?["list"]?[0]?["songmid"]?.ToString();
                if (string.IsNullOrEmpty(songMid)) return null;

                using var lyricReq = new HttpRequestMessage(HttpMethod.Get,
                    $"https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?songmid={songMid}&format=json&nobase64=1");
                lyricReq.Headers.Referrer = new Uri("https://y.qq.com/");
                using var lyricResp = await _httpClient.SendAsync(lyricReq, token);
                string lyricJson = await lyricResp.Content.ReadAsStringAsync(token);

                string? lyric = JObject.Parse(lyricJson)["lyric"]?.ToString();
                if (string.IsNullOrEmpty(lyric)) return null;

                // 部分情况下即便 nobase64=1 也仍是 base64，简单探测一下
                if (!lyric.Contains('['))
                {
                    try { lyric = Encoding.UTF8.GetString(Convert.FromBase64String(lyric)); } catch { }
                }
                return lyric;
            }
            catch { return null; }
        }

        // 4) 酷狗音乐（非官方接口）—— 三步：搜索拿 hash -> 搜词库拿 id/accesskey -> 下载
        private async Task<string?> FetchFromKugouAsync(string title, string artist, CancellationToken token)
        {
            try
            {
                string keyword = Uri.EscapeDataString($"{title} - {artist}");

                string searchUrl = $"https://mobilecdn.kugou.com/api/v3/search/song?format=json&keyword={keyword}&page=1&pagesize=1&showtype=1";
                string searchJson = await _httpClient.GetStringAsync(searchUrl, token);
                string? hash = JObject.Parse(searchJson)["data"]?["info"]?[0]?["hash"]?.ToString();
                if (string.IsNullOrEmpty(hash)) return null;

                string krcUrl = $"https://krcs.kugou.com/search?ver=1&man=yes&client=mobi&keyword={keyword}&hash={hash}";
                string krcJson = await _httpClient.GetStringAsync(krcUrl, token);
                var candidate = JObject.Parse(krcJson)["candidates"]?[0];
                string? id = candidate?["id"]?.ToString();
                string? accessKey = candidate?["accesskey"]?.ToString();
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(accessKey)) return null;

                string downloadUrl = $"https://lyrics.kugou.com/download?ver=1&client=pc&id={id}&accesskey={accessKey}&fmt=lrc&charset=utf8";
                string downloadJson = await _httpClient.GetStringAsync(downloadUrl, token);
                string? contentBase64 = JObject.Parse(downloadJson)["content"]?.ToString();
                if (string.IsNullOrEmpty(contentBase64)) return null;

                return Encoding.UTF8.GetString(Convert.FromBase64String(contentBase64));
            }
            catch { return null; }
        }

        private void ParseLrcText(string lrcContent)
        {
            if (string.IsNullOrEmpty(lrcContent)) return;

            var lines = lrcContent.Replace("\r", "").Split('\n');
            var regex = new Regex(@"\[(?<min>\d+):(?<sec>\d+)[\.:](?<ms>\d+)\](?<text>.*)", RegexOptions.Compiled);

            var tempDict = new Dictionary<int, string>();

            foreach (var line in lines)
            {
                var match = regex.Match(line);
                if (!match.Success) continue;

                int min = int.Parse(match.Groups["min"].Value);
                int sec = int.Parse(match.Groups["sec"].Value);
                string msStr = match.Groups["ms"].Value;

                if (msStr.Length == 1) msStr += "00";
                else if (msStr.Length == 2) msStr += "0";
                int ms = int.Parse(msStr.Substring(0, 3));

                int totalMs = (min * 60 + sec) * 1000 + ms;
                string text = match.Groups["text"].Value.Trim();

                if (!string.IsNullOrEmpty(text))
                {
                    tempDict[totalMs] = text; // 同一时间戳后出现的覆盖前面的，和原逻辑保持一致
                }
            }

            if (tempDict.Count == 0) return;

            var sorted = tempDict.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)).ToList();
            lock (_lyricLock)
            {
                _lyricLines = sorted;
                _lyricCursor = -1;
            }
        }

        // 有序列表 + 游标推进：播放中每次都是 O(1)，只有发生倒退 seek 时才重新定位
        private void UpdateLyricDisplay(int currentMs)
        {
            lock (_lyricLock)
            {
                if (_lyricLines.Count == 0)
                {
                    if (!TxtDynamicLyric.Text.Contains("MINING") &&
                        !TxtDynamicLyric.Text.Contains("CRAFTING") &&
                        !TxtDynamicLyric.Text.Contains("NO LYRICS"))
                    {
                        TxtDynamicLyric.Text = "⛏️ MINING SYNCED LYRICS...";
                    }
                    return;
                }

                // 时间往回跳了（用户拖动了进度条/倒退），游标失效，重新定位
                if (_lyricCursor >= 0 && _lyricLines[_lyricCursor].TimeMs > currentMs)
                {
                    _lyricCursor = -1;
                }

                if (_lyricCursor < 0 && _lyricLines[0].TimeMs > currentMs)
                {
                    return; // 还没到第一句歌词的时间
                }

                int i = _lyricCursor < 0 ? 0 : _lyricCursor;
                while (i + 1 < _lyricLines.Count && _lyricLines[i + 1].TimeMs <= currentMs) i++;

                if (i != _lyricCursor)
                {
                    _lyricCursor = i;
                    string currentLyric = _lyricLines[i].Text.ToUpper();
                    if (TxtDynamicLyric.Text != currentLyric)
                    {
                        TxtDynamicLyric.Text = currentLyric;
                    }
                }
            }
        }
    }
}
