using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Foundation;
using Windows.Media.Control;
using Forms = System.Windows.Forms; // 只用来做系统托盘图标，别名避免跟 System.Windows.Controls 的同名类型撞车

namespace PixelLyric8BitFix
{
    public partial class MainWindow : Window
    {
        // ── 全局热键：Ctrl+Alt+L 随时显示/隐藏，就算窗口被其它程序挡住或者已经收进托盘也能用 ──
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int HotkeyId = 0x4C50; // 随便定的 ID，只要在本进程内不跟别的热键注册撞上就行
        private const uint ModControl = 0x0002;
        private const uint ModAlt = 0x0001;
        private const uint VkL = 0x4C;
        private const int WM_HOTKEY = 0x0312;
        private HwndSource? _hwndSource;

        private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
        private GlobalSystemMediaTransportControlsSession? _currentSession;
        private readonly DispatcherTimer _smoothTimer;
        private readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        private readonly LyricsFetcher _lyricsFetcher; // 抓词逻辑独立成类，这里只负责调用

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

        // ── 歌词同步偏移：歌词框上滚轮调，矫正播放源上报位置跟实际听感之间的固有延迟 ──
        private int _lyricOffsetMs;
        private DispatcherTimer? _syncBadgeHideTimer;

        // ── 卡拉OK扫光效果：按行内估算进度，两种颜色的 Run 拼出"已唱/未唱"；左上角有开关，关了就整行切换 ──
        private int _lastKaraokeLineIndex = -1;
        private int _lastKaraokeSungChars = -1;
        private Brush _karaokeSungBrush = Brushes.White;
        private Brush _karaokeUnsungBrush = Brushes.Gray;

        private string _lastTrackId = "";
        private CancellationTokenSource? _lyricFetchCts;

        // 事件处理器要保留引用，换 session 时才能正确 -= 掉，防止叠加订阅
        private TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs>? _mediaPropsHandler;
        private TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs>? _playbackHandler;
        private TypedEventHandler<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs>? _timelineHandler;

        private readonly AppSettings _settings;

        // ── 客制化主题：只有 _settings.Skin == PlayerSkin.Custom 时才有值 ──────
        private CustomTheme? _customTheme;

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

        // ── 系统托盘：最小化到托盘而不是直接退程序 ──────────────────────────
        private Forms.NotifyIcon? _trayIcon;
        private System.Drawing.Icon? _trayIconHandle;

        // ── Mini 模式：双击缩成一个贴主题的小方块 ────────────────────────
        private bool _isMiniMode;
        private double _preMiniWidth, _preMiniHeight, _preMiniLeft, _preMiniTop;
        private const double MiniBadgeSize = 64;

        public MainWindow() : this(AppSettings.Load()) { }

        public MainWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings ?? new AppSettings();

            // 启动设置页选好的：尺寸预设、皮肤、显示模式
            var (w, h) = _settings.GetWindowSize();
            Width = w;
            Height = h;

            // 上次关闭时存过窗口位置就摆回原位；没存过（第一次用/清过配置）就走 XAML 里的居中
            RestoreWindowPosition(w, h);

            // 选完预设后直接锁死大小，不允许拖拽缩放，避免误拉成全屏
            ResizeMode = ResizeMode.NoResize;
            WindowResizeGrip.Visibility = Visibility.Collapsed;

            ApplySkin(_settings.Skin);
            ApplyDisplayMode(_settings.DisplayMode);

            Loaded += MainWindow_Loaded;
            SourceInitialized += MainWindow_SourceInitialized;
            Closed += (s, e) =>
            {
                // 不管是真退出还是跳去设置页，都把当前位置记下来，下次开窗直接摆回原位
                _settings.WindowLeft = Left;
                _settings.WindowTop = Top;
                _settings.Save();

                _trayIcon?.Dispose();
                _trayIcon = null;
                _trayIconHandle?.Dispose();
                _trayIconHandle = null;

                UnregisterHotKey(new WindowInteropHelper(this).Handle, HotkeyId);
                _hwndSource?.RemoveHook(WndProc);
                _hwndSource = null;

                if (!_navigatingToSettings) Application.Current.Shutdown();
            };

            this.MouseDown += (s, e) => { if (e.ChangedButton == MouseButton.Left) this.DragMove(); };
            // 双击缩成一个贴主题的小方块（Mini 模式），而不是直接退程序或整个消失找不着；
            // 真要彻底藏起来/退出走右键菜单或托盘菜单
            this.MouseDoubleClick += (s, e) => ToggleMiniMode();

            // 右键菜单：改皮肤 / 手动导入歌词 / 隐藏 / 退出（因为窗口本身没有标题栏，双击关闭比较容易误触，多给一个入口）
            var menu = new ContextMenu();
            var itemSkin = new MenuItem { Header = "⚙ 更改皮肤 / 设置" };
            itemSkin.Click += (s, e) => OpenSettingsAndClose();
            var itemImportLrc = new MenuItem { Header = "📄 手动导入歌词 (.lrc)" };
            itemImportLrc.Click += (s, e) => ImportLocalLrc();
            var itemResetOffset = new MenuItem { Header = "🔄 重置歌词偏移" };
            itemResetOffset.Click += (s, e) => AdjustLyricOffset(0, replace: true);
            var itemToggleKaraoke = new MenuItem { Header = "🎤 开关卡拉OK扫光效果" };
            itemToggleKaraoke.Click += (s, e) => ToggleKaraokeEffect();
            var itemHide = new MenuItem { Header = "🔽 隐藏到托盘" };
            itemHide.Click += (s, e) => Hide();
            var itemExit = new MenuItem { Header = "✕ 退出" };
            itemExit.Click += (s, e) => Application.Current.Shutdown();
            menu.Items.Add(itemSkin);
            menu.Items.Add(itemImportLrc);
            menu.Items.Add(itemResetOffset);
            menu.Items.Add(itemToggleKaraoke);
            menu.Items.Add(itemHide);
            menu.Items.Add(new Separator());
            menu.Items.Add(itemExit);
            this.ContextMenu = menu;

            _lyricOffsetMs = _settings.LyricOffsetMs;
            InitTrayIcon();

            // 50ms 只做本地插值计算 + UI 刷新，不再有系统调用，非常轻量
            _smoothTimer = new DispatcherTimer(DispatcherPriority.Render);
            _smoothTimer.Interval = TimeSpan.FromMilliseconds(50);
            _smoothTimer.Tick += SmoothTimer_Tick;

            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            _lyricsFetcher = new LyricsFetcher(_httpClient);
        }

        // 把上次存的坐标摆回去；如果存的位置因为拔显示器/换分辨率之类的原因已经跑到可见范围外了，
        // 就别用了，老老实实回退到 XAML 里配的 CenterScreen，免得窗口开出去找不着
        private void RestoreWindowPosition(double w, double h)
        {
            if (_settings.WindowLeft is not double left || _settings.WindowTop is not double top) return;

            double vLeft = SystemParameters.VirtualScreenLeft;
            double vTop = SystemParameters.VirtualScreenTop;
            double vRight = vLeft + SystemParameters.VirtualScreenWidth;
            double vBottom = vTop + SystemParameters.VirtualScreenHeight;

            bool mostlyOnScreen = left + w > vLeft + 20 && left < vRight - 20 &&
                                   top + h > vTop + 20 && top < vBottom - 20;
            if (!mostlyOnScreen) return;

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }

        // 托盘图标：直接复用程序自己的 exe 图标，双击/菜单里的"显示"都能把窗口拉回来
        private void InitTrayIcon()
        {
            try
            {
                string? exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                _trayIconHandle = !string.IsNullOrEmpty(exePath)
                    ? System.Drawing.Icon.ExtractAssociatedIcon(exePath)
                    : System.Drawing.SystemIcons.Application;

                var trayMenu = new Forms.ContextMenuStrip();
                trayMenu.Items.Add("显示 ZipPlay", null, (s, e) => ShowFromTray());
                trayMenu.Items.Add("⚙ 更改皮肤 / 设置", null, (s, e) => { ShowFromTray(); OpenSettingsAndClose(); });
                trayMenu.Items.Add(new Forms.ToolStripSeparator());
                trayMenu.Items.Add("✕ 退出", null, (s, e) => Application.Current.Shutdown());

                _trayIcon = new Forms.NotifyIcon
                {
                    Icon = _trayIconHandle,
                    Text = "ZipPlay - 像素歌词浮窗",
                    ContextMenuStrip = trayMenu,
                    Visible = true,
                };
                _trayIcon.DoubleClick += (s, e) => ShowFromTray();
            }
            catch (Exception ex)
            {
                // 托盘图标搭不起来（比如某些精简系统组件缺失）不该拖累主功能，静默跳过，但记一笔方便排查
                AppLog.Error("InitTrayIcon", ex);
            }
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        // 歌词框上滚一下滚轮：每格 ±50ms，矫正播放源上报位置跟实际听感之间的固定延迟。
        // 立刻存盘（不等关窗口），这样调好之后哪怕软件崩了/强制关了也不会白调
        private void LyricBox_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            AdjustLyricOffset(e.Delta > 0 ? 50 : -50, replace: false);
        }

        private void AdjustLyricOffset(int deltaOrValue, bool replace)
        {
            _lyricOffsetMs = replace ? deltaOrValue : Math.Clamp(_lyricOffsetMs + deltaOrValue, -5000, 5000);
            _settings.LyricOffsetMs = _lyricOffsetMs;
            _settings.Save();

            string sign = _lyricOffsetMs >= 0 ? "+" : "";
            ShowToast($"🔄 歌词偏移 {sign}{_lyricOffsetMs}ms");
        }

        // 通用的"短暂弹出提示"：歌词偏移、翻译开关都用这个，保证不管有没有实际效果（比如这首歌根本没有翻译），
        // 点击本身都会有个明确、看得见的反馈，不会让人怀疑"是不是没点中"
        private void ShowToast(string message)
        {
            TxtToast.Text = message;
            ToastBadge.Visibility = Visibility.Visible;

            _syncBadgeHideTimer?.Stop();
            _syncBadgeHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _syncBadgeHideTimer.Tick += (s, e) =>
            {
                ToastBadge.Visibility = Visibility.Collapsed;
                _syncBadgeHideTimer?.Stop();
            };
            _syncBadgeHideTimer.Start();
        }

        // 窗口句柄真正建好之后才能注册全局热键；注册失败（比如 Ctrl+Alt+L 已经被别的程序占了）
        // 不影响正常使用，只是热键这一条路走不通，鼠标/托盘那几条路照样能用
        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            try
            {
                var handle = new WindowInteropHelper(this).Handle;
                _hwndSource = HwndSource.FromHwnd(handle);
                _hwndSource?.AddHook(WndProc);
                RegisterHotKey(handle, HotkeyId, ModControl | ModAlt, VkL);
            }
            catch (Exception ex)
            {
                AppLog.Error("RegisterHotKey", ex);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                ToggleVisibilityHotkey();
                handled = true;
            }
            return IntPtr.Zero;
        }

        // 全局热键只管"显示/隐藏"这一个开关，不去搅和 Mini 模式的状态——
        // 隐藏了就拉回来（顺便置顶抢一下焦点），没隐藏（不管是完整状态还是 Mini 状态）就收进托盘
        private void ToggleVisibilityHotkey()
        {
            if (!IsVisible) ShowFromTray();
            else Hide();
        }

        private void ToggleMiniMode()
        {
            if (_isMiniMode) ExitMiniMode();
            else EnterMiniMode();
        }

        // 缩成小方块：原地从中心缩小，而不是跳去左上角；缩小前的尺寸 + 位置都记下来，
        // 不管缩小之后把小方块拖去哪儿，展开时都精确回到这里记的原位（而不是拖到哪儿从哪儿展开）
        private void EnterMiniMode()
        {
            if (_isMiniMode) return;
            _isMiniMode = true;

            _preMiniWidth = Width;
            _preMiniHeight = Height;
            _preMiniLeft = Left;
            _preMiniTop = Top;

            double centerX = Left + Width / 2;
            double centerY = Top + Height / 2;

            MainContentGrid.Visibility = Visibility.Collapsed;
            BtnSettings.Visibility = Visibility.Collapsed;
            UpdateBadge.Visibility = Visibility.Collapsed;

            ApplyMiniBadgeAppearance(_settings.Skin);
            MiniBadge.Visibility = Visibility.Visible;

            Width = MiniBadgeSize;
            Height = MiniBadgeSize;
            Left = centerX - MiniBadgeSize / 2;
            Top = centerY - MiniBadgeSize / 2;
        }

        // 展开：不看小方块现在被拖到哪了，直接照搬 EnterMiniMode 时存的原始位置/尺寸
        private void ExitMiniMode()
        {
            if (!_isMiniMode) return;
            _isMiniMode = false;

            MiniBadge.Visibility = Visibility.Collapsed;
            MainContentGrid.Visibility = Visibility.Visible;
            BtnSettings.Visibility = Visibility.Visible;
            UpdateBadge.Visibility = _updateInfo != null ? Visibility.Visible : Visibility.Collapsed;

            Width = _preMiniWidth;
            Height = _preMiniHeight;
            Left = _preMiniLeft;
            Top = _preMiniTop;
        }

        // 小方块的配色/图标跟当前皮肤走：有现成像素图标（树/篝火/咖啡杯）的就用像素图，
        // 其余皮肤先用 README 里已经在用的那个 emoji 顶一下，观感也算统一
        private void ApplyMiniBadgeAppearance(PlayerSkin skin)
        {
            var t = GetActiveSkinTheme(skin);
            MiniBadge.Background = new SolidColorBrush(t.MiniBg);
            MiniBadge.BorderBrush = new SolidColorBrush(t.MiniBorder);
            MiniBadgeImage.Source = t.MiniIcon();
        }

        // 内置皮肤走 SkinTheme.For；客制化皮肤把用户那份 JSON 现场"翻译"成同一个 SkinTheme 形状，
        // 这样调色板相关的代码（ApplySkinPalette、Mini 小方块）完全不用关心当前是内置还是客制化的。
        //
        // 客制化加载失败（_customTheme 为 null）时要落到 Simple——不能直接丢给 SkinTheme.For(PlayerSkin.Custom)，
        // 那个 switch 没有 Custom 这个分支，会落进默认分支变成 Minecraft 配色，跟 ApplySkin 那边"背景退回简约风"
        // 对不上，会出现"简约风的纯色背景 + Minecraft 的黄绿字"这种背景和文字配色对不上的诡异结果。
        private SkinTheme GetActiveSkinTheme(PlayerSkin skin)
        {
            if (skin == PlayerSkin.Custom)
            {
                return _customTheme != null ? BuildSkinThemeFromCustom(_customTheme) : SkinTheme.For(PlayerSkin.Simple);
            }
            return SkinTheme.For(skin);
        }

        private static SkinTheme BuildSkinThemeFromCustom(CustomTheme custom)
        {
            var colors = custom.Colors!;
            CustomThemeValidator.TryParseHexColor(colors.Title!, out var title);
            CustomThemeValidator.TryParseHexColor(colors.Artist!, out var artist);
            CustomThemeValidator.TryParseHexColor(colors.Accent!, out var accent);
            CustomThemeValidator.TryParseHexColor(colors.Lyric!, out var lyric);
            CustomThemeValidator.TryParseHexColor(string.IsNullOrWhiteSpace(colors.Glow) ? colors.Accent! : colors.Glow, out var glow);
            CustomThemeValidator.TryParseHexColor(colors.LyricBoxBg!, out var lyricBoxBg);
            CustomThemeValidator.TryParseHexColor(colors.LyricBoxBorder!, out var lyricBoxBorder);

            var bgStops = custom.Background!.Stops!
                .Select(s => { CustomThemeValidator.TryParseHexColor(s, out var c); return c; })
                .ToList();
            Color miniBg = bgStops.Count > 0 ? bgStops[0] : Color.FromRgb(0x22, 0x22, 0x22);

            var iconRows = custom.Icon!.Rows!.ToArray();
            var iconPalette = custom.Icon.Palette!.ToDictionary(
                kv => kv.Key[0],
                kv => { CustomThemeValidator.TryParseHexColor(kv.Value, out var c); return c; });
            if (!iconPalette.ContainsKey('.')) iconPalette['.'] = Colors.Transparent;

            return new SkinTheme
            {
                Font = new FontFamily(string.IsNullOrWhiteSpace(custom.Font) ? "Segoe UI" : custom.Font),
                Title = title, Artist = artist, Accent = accent, Lyric = lyric, Glow = glow, GlowBlur = colors.GlowBlur,
                LyricBoxBg = lyricBoxBg, LyricBoxBorder = lyricBoxBorder,
                MiniBg = miniBg, MiniBorder = accent,
                MiniIcon = () => PixelArt.BuildCustomIcon(iconRows, iconPalette),
            };
        }

        // 小方块本身也能拖着走：借用 DragMove() 的原生拖拽循环（跟窗口其它地方拖拽同一套机制，
        // 不用自己手撸鼠标坐标换算，天然兼容多显示器/缩放）。DragMove() 会一直阻塞到用户松开左键，
        // 返回后比较一下位置有没有变化——没变就是单纯点了一下，没有拖动，那就当成"点击展开"。
        private void MiniBadge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            double beforeLeft = Left, beforeTop = Top;
            this.DragMove();
            bool wasDragged = Math.Abs(Left - beforeLeft) > 1 || Math.Abs(Top - beforeTop) > 1;
            if (!wasDragged) ExitMiniMode();
        }

        // 卡拉OK扫光开关：关了就退回最早那种"整行一次性切换"的显示方式，跟加这个效果之前完全一样
        private void ToggleKaraokeEffect()
        {
            _settings.KaraokeEffectEnabled = !_settings.KaraokeEffectEnabled;
            _settings.Save();
            TxtKaraokeToggleIcon.Opacity = _settings.KaraokeEffectEnabled ? 1.0 : 0.5;
            ShowToast(_settings.KaraokeEffectEnabled ? "🎤 卡拉OK效果已开启" : "🎤 卡拉OK效果已关闭");
            _lastKaraokeLineIndex = -1; // 强制下一 tick 按新的开关状态重新渲染当前这句
        }

        private void BtnKaraokeToggle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ToggleKaraokeEffect();
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
            catch (Exception ex)
            {
                _updateInProgress = false;
                TxtUpdateBadge.Text = "⚠️ 下载失败，点击重试";
                AppLog.Error("UpdateBadge download/install", ex);
            }
        }

        // 皮肤：先把 6 套背景层 / 装饰层的显隐都摆对，再套上文字调色板
        private void ApplySkin(PlayerSkin skin)
        {
            // 客制化皮肤要先把 JSON 读出来——加载失败（文件被删/改坏了）就当没有，下面会退回简约风
            _customTheme = skin == PlayerSkin.Custom && !string.IsNullOrEmpty(_settings.CustomThemeFile)
                ? CustomThemeStore.Load(_settings.CustomThemeFile)
                : null;

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
            CloudSkinBg.Visibility = Visibility.Collapsed;
            CandleSkinBg.Visibility = Visibility.Collapsed;
            PlantSkinBg.Visibility = Visibility.Collapsed;
            SunsetSkinBg.Visibility = Visibility.Collapsed;
            CustomSkinBg.Visibility = Visibility.Collapsed;
            MinecraftDecorCanvas.Visibility = Visibility.Collapsed;
            VinylDecorCanvas.Visibility = Visibility.Collapsed;
            LofiDecorCanvas.Visibility = Visibility.Collapsed;
            CampfireDecorCanvas.Visibility = Visibility.Collapsed;
            CassetteDecorCanvas.Visibility = Visibility.Collapsed;
            CandleDecorCanvas.Visibility = Visibility.Collapsed;
            PlantDecorCanvas.Visibility = Visibility.Collapsed;
            CustomIconDecorCanvas.Visibility = Visibility.Collapsed;
            AuroraSnowOverlay.Visibility = Visibility.Collapsed;
            RainOverlay.Visibility = Visibility.Collapsed;
            StarryOverlay.Visibility = Visibility.Collapsed;
            SakuraOverlay.Visibility = Visibility.Collapsed;
            ScanlineOverlay.Visibility = Visibility.Collapsed;
            CloudOverlay.Visibility = Visibility.Collapsed;
            SunsetOverlay.Visibility = Visibility.Collapsed;
            CustomDriftOverlay.Visibility = Visibility.Collapsed;

            // 只有需要顶部那条装饰行的皮肤才留出这一行，其余皮肤更简洁，不留空行；
            // 云朵/海边黄昏的装饰是铺满整个卡片的天空/海面，不是角落小图标，所以不用占这一行；
            // 客制化皮肤留不留这一行取决于用户选的 animation.type 是不是 drift，在 ApplyCustomSkinVisuals 里单独处理
            RowDecor.Height = (skin == PlayerSkin.Minecraft || skin == PlayerSkin.Vinyl ||
                                skin == PlayerSkin.Lofi || skin == PlayerSkin.Campfire ||
                                skin == PlayerSkin.Cassette || skin == PlayerSkin.Candle ||
                                skin == PlayerSkin.Plant)
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

                case PlayerSkin.Cloud:
                    CloudSkinBg.Visibility = Visibility.Visible;
                    CloudOverlay.Visibility = Visibility.Visible;
                    break;

                case PlayerSkin.Candle:
                    CandleSkinBg.Visibility = Visibility.Visible;
                    CandleDecorCanvas.Visibility = Visibility.Visible;
                    ImgCandle.Source = PixelArt.CreateCandle();
                    break;

                case PlayerSkin.Plant:
                    PlantSkinBg.Visibility = Visibility.Visible;
                    PlantDecorCanvas.Visibility = Visibility.Visible;
                    ImgPlant.Source = PixelArt.CreatePlant();
                    break;

                case PlayerSkin.Sunset:
                    SunsetSkinBg.Visibility = Visibility.Visible;
                    SunsetOverlay.Visibility = Visibility.Visible;
                    break;

                case PlayerSkin.Custom:
                    if (_customTheme != null)
                    {
                        ApplyCustomSkinVisuals(_customTheme);
                    }
                    else
                    {
                        // 客制化主题加载失败（文件被删/改坏了）：退回简约风，不能让整个窗口崩掉
                        SimpleSkinBg.Visibility = Visibility.Visible;
                    }
                    break;

                case PlayerSkin.Simple:
                default:
                    SimpleSkinBg.Visibility = Visibility.Visible;
                    break;
            }

            ApplySkinPalette(skin);
        }

        // 客制化皮肤的视觉层：背景（纯色/渐变）+ 用户的像素图标 + 挑一种内置动画"招式"。
        // 跟前面 17 套不一样，这一套完全是数据驱动现场拼出来的，不是写死的 XAML 块。
        private void ApplyCustomSkinVisuals(CustomTheme theme)
        {
            var stops = theme.Background!.Stops!
                .Select(s => { CustomThemeValidator.TryParseHexColor(s, out var c); return c; })
                .ToList();

            Brush bgBrush;
            if (string.Equals(theme.Background.Type, "gradient", StringComparison.OrdinalIgnoreCase) && stops.Count >= 2)
            {
                var gradient = new LinearGradientBrush
                {
                    StartPoint = new System.Windows.Point(0, 0),
                    EndPoint = string.Equals(theme.Background.Direction, "diagonal", StringComparison.OrdinalIgnoreCase)
                        ? new System.Windows.Point(1, 1) : new System.Windows.Point(0, 1),
                };
                for (int i = 0; i < stops.Count; i++)
                {
                    gradient.GradientStops.Add(new GradientStop(stops[i], stops.Count == 1 ? 0 : (double)i / (stops.Count - 1)));
                }
                bgBrush = gradient;
            }
            else
            {
                bgBrush = new SolidColorBrush(stops.Count > 0 ? stops[0] : Colors.Black);
            }

            CustomThemeValidator.TryParseHexColor(theme.Colors!.Accent!, out var accent);

            CustomSkinBg.Background = bgBrush;
            CustomSkinBg.BorderBrush = new SolidColorBrush(accent);
            CustomSkinGlow.Color = accent;
            CustomSkinBg.Visibility = Visibility.Visible;

            var iconPalette = theme.Icon!.Palette!.ToDictionary(
                kv => kv.Key[0],
                kv => { CustomThemeValidator.TryParseHexColor(kv.Value, out var c); return c; });
            if (!iconPalette.ContainsKey('.')) iconPalette['.'] = Colors.Transparent;
            var iconBitmap = PixelArt.BuildCustomIcon(theme.Icon.Rows!.ToArray(), iconPalette);

            string animType = theme.Animation!.Type!.ToLowerInvariant();
            double? customDuration = theme.Animation.Duration;

            if (animType == "drift")
            {
                RowDecor.Height = new GridLength(0);
                CustomDriftOverlay.Visibility = Visibility.Visible;
                CustomDriftIcon1.Source = iconBitmap;
                CustomDriftIcon2.Source = iconBitmap;
                CustomDriftIcon3.Source = iconBitmap;
                StartCustomDriftAnimation(CustomDrift1Transform, customDuration ?? 14, 0);
                StartCustomDriftAnimation(CustomDrift2Transform, (customDuration ?? 14) * 1.35, 2);
                StartCustomDriftAnimation(CustomDrift3Transform, (customDuration ?? 14) * 1.7, 5);
            }
            else
            {
                RowDecor.Height = new GridLength(50);
                CustomIconDecorCanvas.Visibility = Visibility.Visible;
                CustomIcon.Source = iconBitmap;
                StartCustomIconAnimation(animType, customDuration, accent);
            }
        }

        // "飘过型"：同一个图标横向飘过整张卡片，飘到头瞬间重置回最左边——
        // 跟 Cloud/Rain/雪花那几个内置皮肤用的是同一套手法
        private void StartCustomDriftAnimation(TranslateTransform transform, double durationSeconds, double beginDelaySeconds)
        {
            var anim = new DoubleAnimation
            {
                From = -30,
                To = 330,
                Duration = TimeSpan.FromSeconds(SafeDuration(durationSeconds, 14)),
                BeginTime = TimeSpan.FromSeconds(beginDelaySeconds),
                RepeatBehavior = RepeatBehavior.Forever,
            };
            transform.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        // CustomThemeValidator 已经要求 duration 必须 > 0，但这里再兜底一层：万一是加校验之前存的老文件
        // （或者以后校验逻辑本身有疏漏），也不会因为 0/负数时长让 WPF 的动画系统直接抛异常崩掉整个 App。
        private static double SafeDuration(double? value, double fallback) =>
            value.HasValue && value.Value > 0 ? value.Value : fallback;

        // 单图标动画："招式"从 pulse / twinkle / sway / spin / flicker 里选一个，全部用代码现场构造
        // DoubleAnimation，不需要预先在 XAML 里声明 Storyboard 资源
        private void StartCustomIconAnimation(string type, double? customDuration, Color accent)
        {
            CustomIconRotate.Angle = 0;
            CustomIcon.Opacity = 1;
            CustomIconGlow.Color = accent;
            CustomIconGlow.Opacity = 0.6;

            switch (type)
            {
                case "pulse": // 呼吸发光，平缓的明暗循环
                    {
                        var anim = new DoubleAnimation(0.35, 0.75, TimeSpan.FromSeconds(SafeDuration(customDuration, 2.2)))
                        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                        CustomIconGlow.BeginAnimation(DropShadowEffect.OpacityProperty, anim);
                        break;
                    }
                case "twinkle": // 图标本体渐隐渐现，比 pulse 幅度更大、更像星星闪烁
                    {
                        var anim = new DoubleAnimation(0.25, 1.0, TimeSpan.FromSeconds(SafeDuration(customDuration, 1.6)))
                        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                        CustomIcon.BeginAnimation(UIElement.OpacityProperty, anim);
                        break;
                    }
                case "sway": // 以图标底部为轴心左右轻摆，跟绿植角落皮肤同一套手法
                    {
                        var anim = new DoubleAnimation(-8, 8, TimeSpan.FromSeconds(SafeDuration(customDuration, 3.2)))
                        {
                            AutoReverse = true,
                            RepeatBehavior = RepeatBehavior.Forever,
                            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                        };
                        CustomIconRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
                        break;
                    }
                case "spin": // 匀速转圈，跟黑胶/磁带卷盘同一套手法
                    {
                        var anim = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(SafeDuration(customDuration, 4)))
                        { RepeatBehavior = RepeatBehavior.Forever };
                        CustomIconRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
                        break;
                    }
                case "flicker": // 不规则明暗跳动，跟篝火/赛博朋克同一套手法，节奏比 pulse/twinkle 更"毛躁"
                    {
                        double dur = SafeDuration(customDuration, 2.0);
                        var frames = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
                        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.5, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.75, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(dur * 0.15))));
                        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.4, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(dur * 0.3))));
                        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.7, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(dur * 0.42))));
                        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.5, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(dur))));
                        CustomIconGlow.BeginAnimation(DropShadowEffect.OpacityProperty, frames);
                        break;
                    }
            }
        }

        // 每套皮肤自己的字体 / 文字颜色 / 歌词框配色 / 发光效果——具体数据全在 SkinTheme 里，这里只管往控件上贴
        private void ApplySkinPalette(PlayerSkin skin)
        {
            var t = GetActiveSkinTheme(skin);

            TxtSongTitle.FontFamily = t.Font;
            TxtArtist.FontFamily = t.Font;
            TxtTime.FontFamily = t.Font;
            TxtDynamicLyric.FontFamily = t.Font;

            TxtSongTitle.Foreground = new SolidColorBrush(t.Title);
            TxtArtist.Foreground = new SolidColorBrush(t.Artist);
            TxtTime.Foreground = new SolidColorBrush(t.Artist);

            LyricProgressBar.Foreground = new SolidColorBrush(t.Accent);
            LyricProgressBar.Effect = new DropShadowEffect { Color = t.Accent, BlurRadius = 5, ShadowDepth = 0 };

            LyricBox.Background = new SolidColorBrush(t.LyricBoxBg);
            LyricBox.BorderBrush = new SolidColorBrush(t.LyricBoxBorder);

            TxtDynamicLyric.Foreground = new SolidColorBrush(t.Lyric);
            TxtDynamicLyric.Effect = t.GlowBlur > 0
                ? new DropShadowEffect { Color = t.Glow, BlurRadius = t.GlowBlur, ShadowDepth = 0, Opacity = 0.9 }
                : null;

            // 卡拉OK扫光的两种颜色：已经唱到的部分用皮肤原本的歌词亮色（跟以前整行显示时一样），
            // 还没唱到的部分用同一个颜色降透明度，不用给 SkinTheme 单独加字段，任何皮肤配出来都自动协调
            _karaokeSungBrush = new SolidColorBrush(t.Lyric);
            _karaokeUnsungBrush = new SolidColorBrush(Color.FromArgb(0x66, t.Lyric.R, t.Lyric.G, t.Lyric.B));
            _lastKaraokeLineIndex = -1; // 换皮肤了，强制下一 tick 重新渲染一遍当前这句
            _lastKaraokeSungChars = -1;

            TxtKaraokeToggleIcon.Opacity = _settings.KaraokeEffectEnabled ? 1.0 : 0.5;
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
                    PlayerSkin.Cloud => "CloudDriftAnimation",
                    PlayerSkin.Candle => "CandleFlickerAnimation",
                    PlayerSkin.Plant => "PlantSwayAnimation",
                    PlayerSkin.Sunset => "SunsetGlowAnimation",
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
                AppLog.Error("MainWindow_Loaded", ex);
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
            _lastKaraokeLineIndex = -1;

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

            // 4.5 先查本地缓存：这首歌以前成功抓到过的话直接用，完全不用等网络
            string? cachedLrc = LyricsCache.TryGet(trackId);
            if (!string.IsNullOrEmpty(cachedLrc) && LyricsFetcher.IsDurationPlausible(cachedLrc, expectedDuration))
            {
                ParseLrcText(cachedLrc);
                return;
            }

            // 6. 核心：四个免费歌词源并发抓取，谁先给出有效结果就用谁，主线程完全不等待
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

            // + _lyricOffsetMs：用户在歌词框上滚轮调过的手动矫正量，正数让歌词提前显示
            UpdateLyricDisplay((int)position.TotalMilliseconds + _lyricOffsetMs);

            if (_settings.Skin == PlayerSkin.Minecraft)
            {
                UpdateSteveWalkAnimation();
            }
        }

        // 四引擎并发抓词的具体实现全搬到 LyricsFetcher 里了，这里只负责：拿结果 -> 存缓存 -> 解析显示，
        // 或者拿不到结果就报个"没找到"
        private async Task FetchLyricsAnyEngineAsync(string title, string artist, string trackId, TimeSpan expectedDuration, CancellationToken token)
        {
            string? lrc = await _lyricsFetcher.FetchAsync(title, artist, expectedDuration, token);

            if (token.IsCancellationRequested || trackId != _lastTrackId) return; // 已经切歌了，这个结果作废

            if (string.IsNullOrEmpty(lrc))
            {
                Dispatcher.Invoke(() =>
                {
                    lock (_lyricLock)
                    {
                        if (_lyricLines.Count == 0) TxtDynamicLyric.Text = "NO LYRICS FOUND (4 ENGINES TRIED)";
                    }
                });
                return;
            }

            LyricsCache.Save(trackId, lrc);
            ParseLrcText(lrc);
        }

        // 四个引擎都找不到时（小众歌/纯音乐之类）的兜底：手动选一个本地 .lrc 文件用上。
        // 用户自己选的，比自动抓的更可信，不走 IsDurationPlausible 那套版本校验；
        // 存进跟自动抓词共用的那份缓存，下次再放这首歌直接命中，不用重新选一遍。
        private void ImportLocalLrc()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "LRC 歌词文件 (*.lrc)|*.lrc|所有文件 (*.*)|*.*",
                Title = "选择这首歌对应的歌词文件",
            };

            if (dialog.ShowDialog(this) != true) return;

            try
            {
                string content = File.ReadAllText(dialog.FileName);
                if (string.IsNullOrWhiteSpace(content))
                {
                    TxtDynamicLyric.Text = "⚠️ 文件是空的";
                    return;
                }

                _lyricFetchCts?.Cancel(); // 别让还没返回的自动抓词结果晚一步把手动导入的覆盖掉

                if (!string.IsNullOrEmpty(_lastTrackId)) LyricsCache.Save(_lastTrackId, content);
                ParseLrcText(content);
            }
            catch (Exception ex)
            {
                TxtDynamicLyric.Text = "⚠️ 歌词文件读取失败";
                AppLog.Error("ImportLocalLrc", ex);
            }
        }

        private static readonly Regex LrcLineRegex = new(@"\[(?<min>\d+):(?<sec>\d+)[\.:](?<ms>\d+)\](?<text>.*)", RegexOptions.Compiled);

        // 原文歌词和翻译歌词是同一种 LRC 格式，解析逻辑抽成共用的，两边各自只管"解析完存到哪个字段"
        private static List<(int TimeMs, string Text)> ParseLrcLines(string lrcContent)
        {
            if (string.IsNullOrEmpty(lrcContent)) return new List<(int, string)>();

            var lines = lrcContent.Replace("\r", "").Split('\n');
            var tempDict = new Dictionary<int, string>();

            foreach (var line in lines)
            {
                var match = LrcLineRegex.Match(line);
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

            return tempDict.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)).ToList();
        }

        private void ParseLrcText(string lrcContent)
        {
            var sorted = ParseLrcLines(lrcContent);
            if (sorted.Count == 0) return;

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
                    _lastKaraokeLineIndex = -1;
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
                _lyricCursor = i;
                string lineText = _lyricLines[i].Text.ToUpper();

                // 开关关着：退回最早那种"整行一次性切换"，跟加卡拉OK效果之前完全一样，不算 sungChars 那些
                if (!_settings.KaraokeEffectEnabled)
                {
                    if (i != _lastKaraokeLineIndex)
                    {
                        _lastKaraokeLineIndex = i;
                        TxtDynamicLyric.Inlines.Clear();
                        TxtDynamicLyric.Text = lineText;
                    }
                    return;
                }

                // 卡拉OK扫光效果：没有逐字时间戳（免费歌词源基本都只给整行时间），
                // 所以按估算进度算现在唱到第几个字，不是精确逐字同步，但比整行一次性切换有动感得多。
                //
                // 关键点：不能简单地把"这一句到下一句"整段时间都当成唱这句话的时长——
                // 快歌/密集句经常是"唱得很快 + 后面跟一段前奏间奏"，如果按整段区间线性铺开，
                // 扫光会被拖着龟速爬完整个间奏，相对于已经唱完的实际人声就会显得"跟不上"。
                // 所以改成按字数估算一个"大概唱这句要多久"，跟"距离下一句"取较小值——
                // 句子唱得快，扫光就快点扫完，扫完了就停在"整句已点亮"，不会显得滞后；
                // 句子之间本来就挨得很紧（真快歌/说唱）时这个估算值会比区间还大，直接退化成原来的整段线性铺开。
                const double MsPerChar = 70; // 经验值，中等偏快的英文歌唱速，不是精确科学
                int lineStartMs = _lyricLines[i].TimeMs;
                int lineEndMs = (i + 1 < _lyricLines.Count) ? _lyricLines[i + 1].TimeMs : lineStartMs + 4000;
                int estimatedSingMs = Math.Max(300, (int)(lineText.Length * MsPerChar));
                int fillDurationMs = Math.Min(Math.Max(lineEndMs - lineStartMs, 1), estimatedSingMs);
                double progress = (double)(currentMs - lineStartMs) / fillDurationMs;
                progress = Math.Clamp(progress, 0, 1);

                int sungChars = Math.Clamp((int)Math.Round(lineText.Length * progress), 0, lineText.Length);

                // 同一句歌词里，只有"唱到第几个字"这个分割点真的变了才重建 Inlines，
                // 50ms 一 tick 但大多数 tick 分割点不变，不用每次都重新排版
                if (i != _lastKaraokeLineIndex || sungChars != _lastKaraokeSungChars)
                {
                    _lastKaraokeLineIndex = i;
                    _lastKaraokeSungChars = sungChars;
                    RenderKaraokeLine(lineText, sungChars);
                }
            }
        }

        // 用两段不同颜色的 Run 拼出"已经唱到的部分高亮，还没唱到的部分暗淡"的效果。
        // 用 Inlines 而不是裁剪矩形，是因为歌词框允许自动换行（TextWrapping="Wrap"），
        // 换行后裁剪矩形没法正确表达"先填满第一行，再开始填第二行"，Run 拼接则完全不受换行影响。
        private void RenderKaraokeLine(string text, int sungChars)
        {
            TxtDynamicLyric.Inlines.Clear();
            if (sungChars > 0)
            {
                TxtDynamicLyric.Inlines.Add(new Run(text.Substring(0, sungChars)) { Foreground = _karaokeSungBrush });
            }
            if (sungChars < text.Length)
            {
                TxtDynamicLyric.Inlines.Add(new Run(text.Substring(sungChars)) { Foreground = _karaokeUnsungBrush });
            }
        }

    }
}
