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
    // 这个类拆成了好几个文件（partial class），按关注点分开，单个文件都不会太大：
    //   MainWindow.xaml.cs       —— 这个文件：字段声明、构造函数、窗口生命周期、播放进度锚点、主 tick
    //   MainWindow.Tray.cs       —— 系统托盘 + 全局热键
    //   MainWindow.MiniMode.cs   —— Mini 模式（双击缩成小方块）
    //   MainWindow.Skins.cs      —— 皮肤应用/调色板/客制化主题渲染/MC 皮肤 Steve 走路
    //   MainWindow.Lyrics.cs     —— 抓词/解析/歌词同步偏移/卡拉OK扫光/双语歌词
    //   MainWindow.Updates.cs    —— 检查更新 + 一键下载安装
    //   MainWindow.PlaybackControls.cs —— 上一首/播放-暂停/下一首 + 进度条拖拽跳转
    //   MainWindow.SkinInteractions.cs —— 皮肤音乐律动（黑胶/磁带机/篝火/Minecraft/星空/雨夜/极光雪夜/樱花/CRT/赛博朋克 + 客制化主题）+ 装饰物可点击反馈（Steve/篝火）
    // 拆开纯粹是文件组织，行为跟拆之前完全一样，只是不用再在一个 1300+ 行的文件里翻了。
    public partial class MainWindow : Window
    {
        private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
        private GlobalSystemMediaTransportControlsSession? _currentSession;
        private readonly DispatcherTimer _smoothTimer;
        private readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };

        // 专门给"下载更新安装包"用的另一个 HttpClient，超时给得长很多——不能跟上面那个共用：
        // 上面那个 4 秒超时是为了让抓词/查版本这类应该秒回的小请求卡住时能快速放弃换下一个引擎，
        // 但安装包动辄几十 MB，4 秒经常连一半都下不完，HttpClient.Timeout 管的是整个请求（包括读响应体），
        // 不是只管建立连接，用短超时的那个客户端下载会大概率半路被 TaskCanceledException 打断。
        private readonly HttpClient _downloadHttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

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

        // ── 双语歌词：翻译行独立一份时间轴（目前只有网易云引擎会给），列表通常不长，
        // 按当前播放位置线性找最后一个"已经到时间"的翻译行就够了，不用像主歌词那样维护游标 ──
        private List<(int TimeMs, string Text)> _translationLines = new();

        // ── 歌词同步偏移：歌词框上滚轮调，矫正播放源上报位置跟实际听感之间的固有延迟 ──
        private int _lyricOffsetMs;
        private DispatcherTimer? _syncBadgeHideTimer;

        // ── 进度条拖拽跳转：拖动中这个是 true，SmoothTimer_Tick 那边就不再用播放位置覆盖进度图标的位置，
        // 不然会跟用户的拖动手势打架，见 MainWindow.PlaybackControls.cs ──
        private bool _isDraggingSeek;

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
        private readonly TrayIconManager _trayIconManager = new();

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

        // ── Mini 模式：双击缩成一个贴主题的小方块 ────────────────────────
        private bool _isMiniMode;
        private double _preMiniWidth, _preMiniHeight, _preMiniLeft, _preMiniTop;
        private const double MiniBadgeSize = 64; // 方块本体大小，要跟 MainWindow.xaml 里 MiniBadge 的 Width/Height 对上

        // Mini 状态下窗口比方块本体（64）大一整圈，多出来的这圈放两层同心粒子环；方块视觉大小不变，
        // 只是 Mini 窗口整体比原来（就是方块本身那么大）大了一圈，留出地方给粒子环
        private const double MiniModeWindowSize = 140;

        // ── Mini 模式的像素粒子环：抓真实系统音频（WASAPI 回环），只有 Mini 状态下才会跑，见 MainWindow.MiniMode.cs ──
        // 内圈跟着整体响度一直呼吸，外圈只有鼓点/高音冲击的时候才会多出来，两圈分别对应 AudioVisualizerSnapshot
        // 的 OverallLevel / BeatPulse，不再是"每颗粒子各绑一个频段"那种容易看着杂乱的做法
        private readonly AudioVisualizer _audioVisualizer = new();
        private (System.Windows.Shapes.Rectangle Element, bool IsOuterRing)[]? _miniVisualizerParticles;

        // ── 皮肤音乐律动：跟 Mini 模式共用同一个 _audioVisualizer，见 MainWindow.SkinInteractions.cs。
        // _isMusicReactiveSkin 代表"当前这套皮肤（或客制化主题勾了 musicReactive）参与律动，该不该抓音频"；
        // _musicReactiveStoryboards 里放的是所有"额外以 isControllable=true 方式启动、可以实时调 SpeedRatio"
        // 的 Storyboard——可能是 0 个（比如只有 Steve 跳跃这种一次性动作、没有连续循环可调速的皮肤）、
        // 1 个（大多数内置皮肤），也可能是好几个（客制化主题 drift/fall 那种一次起好几个独立图标的情况）。
        // 用 List 而不是单个可空字段，就是为了让这几种情况共用同一套调速循环，不用分开写。
        private bool _isMusicReactiveSkin;
        private readonly List<Storyboard> _musicReactiveStoryboards = new();

        public MainWindow() : this(AppSettings.Load()) { }

        public MainWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings ?? new AppSettings();
            _audioVisualizer.ApplySensitivity(_settings.VisualizerSensitivity);

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

                _trayIconManager.Dispose();

                UnregisterHotKey(new WindowInteropHelper(this).Handle, HotkeyId);
                _hwndSource?.RemoveHook(WndProc);
                _hwndSource = null;

                _downloadHttpClient.Dispose();
                _audioVisualizer.Dispose();

                if (!_navigatingToSettings) Application.Current.Shutdown();
            };

            this.MouseDown += (s, e) => { if (e.ChangedButton == MouseButton.Left) this.DragMove(); };
            // 双击缩成一个贴主题的小方块（Mini 模式），而不是直接退程序或整个消失找不着；
            // 真要彻底藏起来/退出走右键菜单或托盘菜单
            this.MouseDoubleClick += (s, e) => ToggleMiniMode();

            // 隐藏到托盘时律动条也没必要继续抓音频/算 FFT 白费功夫，收起来就停、拉回来（如果还在 Mini 状态）就续上
            this.IsVisibleChanged += (s, e) => SyncAudioVisualizerState();

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
            var itemToggleBilingual = new MenuItem { Header = "🌐 开关双语歌词" };
            itemToggleBilingual.Click += (s, e) => ToggleBilingualLyrics();
            var itemHide = new MenuItem { Header = "🔽 隐藏到托盘" };
            itemHide.Click += (s, e) => Hide();
            var itemExit = new MenuItem { Header = "✕ 退出" };
            itemExit.Click += (s, e) => Application.Current.Shutdown();
            menu.Items.Add(itemSkin);
            menu.Items.Add(itemImportLrc);
            menu.Items.Add(itemResetOffset);
            menu.Items.Add(itemToggleKaraoke);
            menu.Items.Add(itemToggleBilingual);
            menu.Items.Add(itemHide);
            menu.Items.Add(new Separator());
            menu.Items.Add(itemExit);
            this.ContextMenu = menu;

            _lyricOffsetMs = _settings.LyricOffsetMs;
            InitTrayIcon();
            // UpdateBilingualToggleIcon() 不用在这再调一遍——ApplySkin（构造函数前面已经调过）末尾的
            // ApplySkinPalette 里现在会顺手调一次，保证发光颜色（跟着皮肤强调色）和亮不亮这两件事
            // 用的是同一份最新状态，不用依赖"构造函数里两处调用顺序刚好对" 这种隐式约定

            // 50ms 只做本地插值计算 + UI 刷新，不再有系统调用，非常轻量
            _smoothTimer = new DispatcherTimer(DispatcherPriority.Render);
            _smoothTimer.Interval = TimeSpan.FromMilliseconds(50);
            _smoothTimer.Tick += SmoothTimer_Tick;

            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            _downloadHttpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
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

        // 通用的"短暂弹出提示"：歌词偏移、卡拉OK/双语开关都用这个，保证不管有没有实际效果（比如这首歌根本没有翻译），
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
                    PlayerSkin.Arcade => "ArcadeMarqueeAnimation",
                    PlayerSkin.Invaders => "InvadersPulseAnimation",
                    PlayerSkin.City => "CityGlowAnimation",
                    _ => null,
                };
                // 皮肤音乐律动：跟着音乐"加速"这一类内置皮肤，加上 Minecraft（走路变速 + Steve 跳跃），
                // 加上客制化主题勾了 musicReactive 开关的情况。lofi、烛光冥想两套刻意保持安静，不接进去。
                // 见 MainWindow.SkinInteractions.cs 的 UpdateMusicReactiveSkin。
                _isMusicReactiveSkin = _settings.Skin is PlayerSkin.Vinyl or PlayerSkin.Cassette
                    or PlayerSkin.Campfire or PlayerSkin.Minecraft
                    or PlayerSkin.Starry or PlayerSkin.Rain or PlayerSkin.Aurora
                    or PlayerSkin.Sakura or PlayerSkin.Crt or PlayerSkin.Cyberpunk
                    or PlayerSkin.Arcade or PlayerSkin.Invaders or PlayerSkin.City
                    || (_settings.Skin == PlayerSkin.Custom && _customTheme?.Animation?.MusicReactive == true);

                if (ambientAnimationKey != null)
                {
                    var ambientStoryboard = (Storyboard)this.Resources[ambientAnimationKey];

                    // 这几套的 ambient Storyboard 本身就是连续循环（转速/闪烁/飘落节奏……），
                    // 需要跟着音乐实时调 SpeedRatio，得用 isControllable=true 启动；
                    // 其余不参与律动的皮肤走原来的启动方式就行。
                    bool needsControllableStoryboard = _settings.Skin is PlayerSkin.Vinyl or PlayerSkin.Cassette or PlayerSkin.Campfire
                        or PlayerSkin.Starry or PlayerSkin.Rain or PlayerSkin.Aurora or PlayerSkin.Sakura
                        or PlayerSkin.Crt or PlayerSkin.Cyberpunk
                        or PlayerSkin.Arcade or PlayerSkin.Invaders or PlayerSkin.City;
                    if (needsControllableStoryboard && _settings.SkinAudioReactiveEnabled)
                    {
                        ambientStoryboard.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
                        _musicReactiveStoryboards.Add(ambientStoryboard);
                    }
                    else
                    {
                        ambientStoryboard.Begin(this);
                    }
                }

                if (_isMusicReactiveSkin && _settings.SkinAudioReactiveEnabled)
                {
                    SyncAudioVisualizerState(); // 现在多了个理由要抓音频，跟 Mini 模式共用同一份判断逻辑
                }

                if (_settings.Skin == PlayerSkin.Minecraft)
                {
                    // Steve 走路的速度也跟着音乐变——只有律动开关开着才用 isControllable 的方式起这个循环，
                    // 关掉的时候走原来的写法，不多这一层开销
                    StartSteveWalking(_isMusicReactiveSkin && _settings.SkinAudioReactiveEnabled);
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
                    UpdatePlayPauseIcon();
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

            LyricProgressBar.Maximum = _totalDuration.TotalMilliseconds > 0 ? _totalDuration.TotalMilliseconds : 100;

            // 拖动进度条的时候不要用播放位置覆盖进度条/图标/时间文字——那些是用户手上正在控制、正在看的东西，
            // 覆盖了就会跟拖动手势打架（时间文字按真实播放位置涨，进度条却停在拖动目标，两边对不上；
            // 松手前进度条自己先跳回真实播放位置，很难看）。拖动时的时间文字由 UpdateSeekPreview 自己写。
            if (!_isDraggingSeek)
            {
                TxtTime.Text = FormatPositionText(position, _totalDuration);
                LyricProgressBar.Value = position.TotalMilliseconds;
                UpdateSeekThumbPosition();
            }

            // + _lyricOffsetMs：用户在歌词框上滚轮调过的手动矫正量，正数让歌词提前显示
            UpdateLyricDisplay((int)position.TotalMilliseconds + _lyricOffsetMs);

            if (_settings.Skin == PlayerSkin.Minecraft)
            {
                UpdateSteveWalkAnimation();
            }

            if (_isMiniMode)
            {
                UpdateMiniVisualizer();
            }

            if (_isMusicReactiveSkin && _settings.SkinAudioReactiveEnabled)
            {
                UpdateMusicReactiveSkin();
            }
        }
    }
}
