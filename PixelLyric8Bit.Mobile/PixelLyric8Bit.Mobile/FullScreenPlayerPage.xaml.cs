using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;
using PixelLyric8BitFix;

#if __ANDROID__
using Android.Content;
using Android.Media;
using Android.OS;
using Android.Views;
using AndroidMediaSession = Android.Media.Session;
#endif

namespace PixelLyric8Bit.Mobile;

/// <summary>
/// 全屏播放——只有 Android 有意义的手机版专属功能：不是悬浮窗那种飘在别的 App 上面的一小条，是把
/// 这个页面撑满整个屏幕（隐藏状态栏/导航栏），大字显示当前这句歌词，配一条播放控制条（上一首/
/// 播放暂停/下一首），适合"专心看歌词"这种场景——不想再开着悬浮窗那个小窗口。
///
/// 跟悬浮窗（Platforms/Android/FloatingOverlayService.cs）是两个完全独立的东西，故意不共享同一份
/// MediaController/歌词抓取状态：这边只在这个页面打开的这段时间里才绑定媒体会话、按 200ms 一次
/// 本地插值播放位置（同一套 PlaybackPositionEstimator 数学），退出页面就整个拆掉。不共享状态换来
/// 两个好处：悬浮窗完全不用改一行代码（零回归风险）；也不会因为
/// 两边各记一份"听了多久"而把统计数字算重复——所以这个页面本身不碰 ListeningStatsFileStore/
/// AchievementCalculator，统计这件事继续全部交给悬浮窗（如果开着的话）去记，两边同时开着也没事，
/// 只是这个页面自己不再重复攒一份。
///
/// 播放/暂停/上一首/下一首直接调系统 MediaController.GetTransportControls()——大多数音乐 App 都接了
/// 这几个标准媒体按键（跟耳机上的按键、通知栏媒体控件走的是同一套系统 API，不需要跟具体某个 App
/// 有什么私有协议）。按下去之后 UI 不会立刻自己翻转状态，是等 PlaybackCallback 收到系统真的汇报
/// 回来的新状态才刷新——跟悬浮窗、桌面版一个原则：只信系统汇报的事实，不本地"乐观"假装已经生效。
///
/// 皮肤/主题：背景、播放/暂停键的强调色、歌词文字色都跟着 SkinPage 那边选的当前皮肤走（内置皮肤
/// 或客制化主题都认），见 ApplyPalette——这边直接读 MobileSettingsStore.SelectedSkinId 现算一份
/// MobileSkinPalette，跟悬浮窗 FloatingOverlayService.ApplySkin 是同一个数据源，只是悬浮窗画的是
/// 原生 Android View，这边是 Uno 的 XAML 元素，画法不一样但配色应该看起来是一致的。皮肤在设置页
/// 被换掉（哪怕这个全屏页正开着）也会跟着立刻换，见 StartSession 里注册的那个 SettingsPrefListener，
/// 跟悬浮窗"开着也能换色"是同一个用意。
///
/// 屏幕常亮：这个页面存在的意义就是"盯着看歌词"，没有触摸操作的话系统正常会按超时自动锁屏/变暗，
/// 那就跟这个功能的初衷完全相反了——EnterImmersiveFullscreen 里顺手把 KeepScreenOn 开了，退出页面
/// 记得在 ExitImmersiveFullscreen 里关掉，不然退出这个页面之后整个 App 其它地方也会一直不锁屏。
///
/// 播放控制三个按钮（上一首/播放暂停/下一首）不用 emoji 字符——emoji 长什么样是系统字体决定的，
/// 不同手机厂商画出来风格都不一样，也没法配色。改成跟悬浮窗图标同一套 PixelIconRenderer 现画的
/// 像素图标（见 BuildIconBitmap），颜色也是现算的：播放/暂停键底色是皮肤强调色，图标用
/// RgbaColor.PickReadableForeground 保证跟底色对比度够；上一首/下一首底色是强调色跟黑混过调暗
/// 一档（MixTowardBlack），图标同样保证读得清。客制化主题的 icon.frames 逐帧动画也接上了（见
/// IconFrameTick），跟悬浮窗/CustomThemePage 预览是同一个"tick 累计时间、攒够一帧才切"思路。
///
/// 无操作几秒顶部条自动隐藏（HideTopBar）、播放进度条能拖动 seek（ProgressSlider_Pointer*）、
/// 长歌词自动缩小字号（LyricFontSizeFor）——这几个都是纯 UI 层的打磨，不影响上面这些数据/主题
/// 相关的逻辑。这几个"定期做点什么"的活全靠 Android 原生 Handler.PostDelayed 自己排下一次，不是
/// Microsoft.UI.Xaml.DispatcherTimer——见 _mainHandler 字段那段注释，这是趟出来的坑，不是随手选的。
/// </summary>
public sealed partial class FullScreenPlayerPage : Page
{
    public FullScreenPlayerPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
#if __ANDROID__
        EnterImmersiveFullscreen();
        StartSession();
#else
        TxtAndroidOnlyHint.Visibility = Visibility.Visible;
#endif
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
#if __ANDROID__
        StopSession();
        ExitImmersiveFullscreen();
#endif
    }

    private void BtnExit_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    // 无操作几秒后顶部条自动隐藏（RootGrid_PointerPressed 处理点空白处唤出），这几个按钮方法签名
    // 必须留在 #if __ANDROID__ 外面——XAML 里的 Click="BtnXxx_Click"/PointerPressed 是不分平台的，
    // 编译到非 Android 的 net10.0 这个 TFM 时也要能找到同名方法，找不到直接编译报错（原来整个方法
    // 连签名一起塞进 #if 里，net10.0 这个 TFM 编译不过，就是这个坑）。方法体本身该干的事只有
    // Android 才有意义（调系统 MediaController/摸自己的计时器），照样包在 #if 里，只是签名露在外面。
    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
#if __ANDROID__
        ShowTopBarAndResetIdleTimer();
#endif
    }

    private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
    {
#if __ANDROID__
        ShowTopBarAndResetIdleTimer();
        var transportControls = _controller?.GetTransportControls();
        if (transportControls == null) return;
        if (_isPlaying) transportControls.Pause(); else transportControls.Play();
#endif
    }

    private void BtnPrev_Click(object sender, RoutedEventArgs e)
    {
#if __ANDROID__
        ShowTopBarAndResetIdleTimer();
        _controller?.GetTransportControls()?.SkipToPrevious();
#endif
    }

    private void BtnNext_Click(object sender, RoutedEventArgs e)
    {
#if __ANDROID__
        ShowTopBarAndResetIdleTimer();
        _controller?.GetTransportControls()?.SkipToNext();
#endif
    }

    private void ProgressSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
#if __ANDROID__
        ShowTopBarAndResetIdleTimer();
        _isDraggingSeek = true;
#endif
    }

    private void ProgressSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
#if __ANDROID__
        CommitSeekAndStopDragging();
#endif
    }

    // 拖到一半触摸被系统半路抢走（判定成别的手势）时只会收到这个事件、收不到 PointerReleased——
    // 见 XAML 里这个 Slider 上那段注释，不接这个的话 _isDraggingSeek 会卡在 true 出不来，进度条就
    // 像卡死了一样怎么点都不再跟着真实播放位置走。
    private void ProgressSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
#if __ANDROID__
        if (!_isDraggingSeek) return;
        CommitSeekAndStopDragging();
#endif
    }

#if __ANDROID__
    private void CommitSeekAndStopDragging()
    {
        _isDraggingSeek = false;
        _controller?.GetTransportControls()?.SeekTo((long)ProgressSlider.Value);
    }
#endif

    // 拖动过程中把下面的时间文字换成跟着手指走的目标位置，不是继续显示"实际正在播放"的位置——见
    // XAML 里这个 Slider 上那段注释，不然拖动条明明在动、时间数字却像没反应一样，看起来跟没法拖没
    // 两样。这个方法在非拖动状态下也会触发（比如 RenderTick 每 200ms 回写 Value 那次），但那种情况
    // 不该覆盖 UpdateLyricUi 已经写好的真实进度文字，所以先判断 _isDraggingSeek 再动手。
    private void ProgressSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
#if __ANDROID__
        if (!_isDraggingSeek) return;
        TxtProgress.Text = FormatProgress(TimeSpan.FromMilliseconds(e.NewValue), _totalDuration);
#endif
    }

#if __ANDROID__
    private const int RenderIntervalMs = 200;
    private const int ControllerRebindRetryIntervalMs = 3000;

    private readonly HttpClient _httpClient = new();
    private LyricsFetcher? _lyricsFetcher;
    private LyricsCacheStore? _lyricsCache;
    private LyricsTranslator? _translator;

    // 这个页面所有"定期做点什么"的活（歌词渲染 tick / 图标逐帧动画 / 无操作自动隐藏顶部条）都靠这
    // 一个 Handler 自己 PostDelayed 重新排下一次，不是 Microsoft.UI.Xaml.DispatcherTimer——实测在
    // 这套 Uno + SkiaRenderer 的 Android 环境下，DispatcherTimer 只有手动调一次触发得动
    // （比如 StartSession 里那次立即调用），Tick 事件本身不会自己反复触发，猜测是跟 Skia 那边"没有
    // 东西要重绘就不 pump 渲染循环、DispatcherTimer 挂在这个循环上"有关系；FloatingOverlayService
    // 那边的 Handler.PostDelayed 是 Android 原生机制，不挂在 Uno 的渲染循环上，一直稳定，所以这个
    // 页面也改成同一套，不是偏好问题，是趟出来的坑。
    private Handler? _mainHandler;
    private Action? _renderTickAction;
    private Action? _iconFrameTickAction;
    private Action? _hideTopBarAction;

    private AndroidMediaSession.MediaSessionManager? _sessionManager;
    private ActiveSessionsListener? _activeSessionsListener;
    private AndroidMediaSession.MediaController? _controller;
    private PlaybackCallback? _controllerCallback;

    private string _currentTitle = "";
    private string _currentArtist = "";
    private TimeSpan _totalDuration = TimeSpan.Zero;
    private TimeSpan _anchorPosition = TimeSpan.Zero;
    private DateTimeOffset _anchorTime = DateTimeOffset.Now;
    private bool _isPlaying;
    private double _playbackRate = 1.0;

    private string? _fetchedForTrackKey;
    private List<(int TimeMs, string Text)>? _currentLines;
    private string? _currentLrcContent;
    private List<(int TimeMs, string Text)>? _translationLines;
    private bool _fetchInFlight;
    private CancellationTokenSource? _fetchCts;

    private bool _karaokeEnabled;
    private bool _bilingualEnabled;
    private int _syncOffsetMs;
    private DateTimeOffset _lastControllerRebindAttempt = DateTimeOffset.MinValue;

    // ── 皮肤/主题，见 ApplyPalette ────────────────────────────────────────────────
    private MobileCustomThemeStore? _customThemeStore;
    private MobileSkinPalette _palette = MobileSkinCatalog.Find(null);
    private SettingsPrefListener? _settingsPrefListener;
    private WriteableBitmap? _playIconBitmap;
    private WriteableBitmap? _pauseIconBitmap;

    // ── 客制化主题 icon.frames 逐帧动画，见 IconFrameTick——跟 CustomThemePage 预览、
    // FloatingOverlayService.IconFrameTick 同一个"tick 累计时间、攒够一帧才切"思路，null 或者只有
    // 1 张就是静态图标，tick 直接跳过 ─────────────────────────────────────────────────
    private const int IconFrameTickIntervalMs = 100;
    private WriteableBitmap[]? _iconFrameBitmaps;
    private double _iconFrameDurationSeconds;
    private double _iconFrameElapsedMs;
    private int _iconFrameIndex;

    // ── 无操作几秒顶部条自动隐藏，见 HideTopBar/ShowTopBarAndResetIdleTimer ───────────────────────
    private const int IdleHideSeconds = 4;

    // ── 播放进度条拖动，见 ProgressSlider_PointerPressed/Released——拖着的时候 UpdateLyricUi 不能
    // 顺手把 Value 拽回真实播放位置，不然手指还没松开进度条就自己跳回去，根本拖不动 ───────────────
    private bool _isDraggingSeek;

    // ── 播放控制三个按钮的像素图标（8x8，'#' 是唯一用到的颜色字符，具体什么颜色由 ApplyPalette
    // 现算），见类顶部注释为什么不用 emoji。Play/SkipNext 是同一个"右指三角"形状思路，Prev 是
    // SkipNext 整行反过来（见 CustomThemePage 同类注释里"闭包坑"那种手动核对，这边是几何镜像，
    // 已经手动验证过：原图案 [0,W-1] 反过来变成 [8-W,7]，右指三角镜像后变成左指三角）──────────────
    private static readonly string[] PlayIconRows =
    {
        "#.......", "##......", "###.....", "####....",
        "####....", "###.....", "##......", "#.......",
    };
    private static readonly string[] PauseIconRows =
    {
        ".##..##.", ".##..##.", ".##..##.", ".##..##.",
        ".##..##.", ".##..##.", ".##..##.", ".##..##.",
    };
    private static readonly string[] NextIconRows =
    {
        "#.....#.", "##....#.", "###...#.", "####..#.",
        "####..#.", "###...#.", "##....#.", "#.....#.",
    };
    private static readonly string[] PrevIconRows =
    {
        ".#.....#", ".#....##", ".#...###", ".#..####",
        ".#..####", ".#...###", ".#....##", ".#.....#",
    };

    // ── 沉浸式全屏：隐藏状态栏 + 导航栏 ──────────────────────────────────────────────
    // 用的是经典的 View.SystemUiVisibility 位标志，不是更新的 WindowInsetsController——从 API 16
    // 起就有，新 API level 上标了"过时"但功能照样好用，不用为了这一个效果额外引入 AndroidX
    // WindowInsetsControllerCompat 这个依赖。ImmersiveSticky 保证用户从屏幕边缘划一下唤出系统栏时
    // 是半透明叠加、松手自动收回，不会把这个播放页顶下去露出一截，见 MainActivity.Current 注释——
    // 拿不到 Activity（理论上不该发生，保险起见）就静默放弃，不影响这个页面本身正常显示歌词/操作。
    private void EnterImmersiveFullscreen()
    {
        try
        {
            var window = Droid.MainActivity.Current?.Window;
            if (window == null) return;
            var decorView = window.DecorView;
            if (decorView == null) return;
            decorView.SystemUiVisibility = (StatusBarVisibility)(
                SystemUiFlags.ImmersiveSticky | SystemUiFlags.LayoutStable | SystemUiFlags.LayoutHideNavigation |
                SystemUiFlags.LayoutFullscreen | SystemUiFlags.HideNavigation | SystemUiFlags.Fullscreen);

            // 屏幕常亮——这个页面就是给人盯着看歌词的，没有触摸操作的话系统会照常按超时锁屏/变暗，
            // 跟这功能的初衷反着来，见类顶部注释。退出的时候记得在 ExitImmersiveFullscreen 里清掉。
            window.AddFlags(WindowManagerFlags.KeepScreenOn);
        }
        catch
        {
            // 沉浸式失败不影响这个页面本身正常显示歌词/操作播放，静默放弃
        }
    }

    private void ExitImmersiveFullscreen()
    {
        try
        {
            var window = Droid.MainActivity.Current?.Window;
            if (window == null) return;
            window.ClearFlags(WindowManagerFlags.KeepScreenOn);

            var decorView = window.DecorView;
            if (decorView == null) return;
            decorView.SystemUiVisibility = (StatusBarVisibility)SystemUiFlags.Visible;
        }
        catch
        {
        }
    }

    // ── 媒体会话绑定 + 歌词抓取：跟 FloatingOverlayService 同一个思路的精简版，见类顶部注释为什么
    // 故意不共享同一份状态 ──────────────────────────────────────────────────────────

    private void StartSession()
    {
        _lyricsFetcher = new LyricsFetcher(_httpClient);
        var context = global::Android.App.Application.Context;
        // 跟悬浮窗共用同一个缓存目录（CacheDir/lyrics_cache）——同一首歌不用重新联网抓一遍，两边
        // 各自 new 一个 LyricsCacheStore 实例，不是共享同一个对象引用，但读写的是同一批文件
        _lyricsCache = new LyricsCacheStore(Path.Combine(context.CacheDir!.AbsolutePath, "lyrics_cache"));
        _translator = new LyricsTranslator(_httpClient);

        // 客制化主题存档也是同一份磁盘目录（FilesDir/custom_themes），跟 SkinPage/CustomThemePage
        // 各自 new 一个 MobileCustomThemeStore 实例、读写同一批文件是同一个套路，见那两个页面注释
        _customThemeStore = new MobileCustomThemeStore(Path.Combine(context.FilesDir!.AbsolutePath, "custom_themes"));

        _karaokeEnabled = Droid.MobileSettingsStore.KaraokeEnabled;
        _bilingualEnabled = Droid.MobileSettingsStore.BilingualEnabled;
        _syncOffsetMs = Droid.MobileSettingsStore.SyncOffsetMs;
        ApplyPalette();

        // 皮肤在设置页被换掉（哪怕这个全屏页正开着）也要跟着立刻换，见类顶部注释——跟悬浮窗
        // RefreshSettingsFromStore 是同一个用意，这边顺手把同步偏移/卡拉OK/双语这几个设置也一起
        // 重新读一遍，免得再单独订阅一遍
        _settingsPrefListener = new SettingsPrefListener(() =>
        {
            _karaokeEnabled = Droid.MobileSettingsStore.KaraokeEnabled;
            _bilingualEnabled = Droid.MobileSettingsStore.BilingualEnabled;
            _syncOffsetMs = Droid.MobileSettingsStore.SyncOffsetMs;
            ApplyPalette();
        });
        Droid.MobileSettingsStore.RegisterChangeListener(_settingsPrefListener);

        _mainHandler = new Handler(Looper.MainLooper!);

        _sessionManager = (AndroidMediaSession.MediaSessionManager)context.GetSystemService(Context.MediaSessionService)!;
        var component = new ComponentName(context, Java.Lang.Class.FromType(typeof(Droid.MediaNotificationListenerService)));
        _activeSessionsListener = new ActiveSessionsListener(RebindController);
        _sessionManager.AddOnActiveSessionsChangedListener(_activeSessionsListener, component);
        RebindController(Droid.MediaNotificationListenerService.Instance?.GetActiveMediaControllers());

        _renderTickAction = RenderTick;
        _mainHandler.Post(_renderTickAction); // 立刻先画一次，不用等第一个 tick 才有内容，之后 RenderTick 自己 PostDelayed 排下一次

        _iconFrameTickAction = IconFrameTick;
        _mainHandler.Post(_iconFrameTickAction);

        _hideTopBarAction = HideTopBar;
        ShowTopBarAndResetIdleTimer(); // 刚打开这一刻顶部条先露出来，不是一进来就是隐藏的
    }

    private void StopSession()
    {
        // 一次性清掉这个 Handler 上排着的所有 PostDelayed（render/iconFrame/hideTopBar 三个都在同
        // 一个 Handler 上，不用像 FloatingOverlayService 那样逐个 RemoveCallbacks），退出页面这几个
        // 活动一个都不该再继续跑
        _mainHandler?.RemoveCallbacksAndMessages(null);
        _mainHandler = null;
        _renderTickAction = null;
        _iconFrameTickAction = null;
        _hideTopBarAction = null;
        _iconFrameBitmaps = null;

        if (_settingsPrefListener != null)
        {
            Droid.MobileSettingsStore.UnregisterChangeListener(_settingsPrefListener);
        }
        _settingsPrefListener = null;

        if (_sessionManager != null && _activeSessionsListener != null)
        {
            _sessionManager.RemoveOnActiveSessionsChangedListener(_activeSessionsListener);
        }
        _sessionManager = null;
        _activeSessionsListener = null;

        if (_controller != null && _controllerCallback != null)
        {
            _controller.UnregisterCallback(_controllerCallback);
        }
        _controller = null;
        _controllerCallback = null;

        _fetchCts?.Cancel();
        _currentLines = null;
        _translationLines = null;
    }

    // ── 皮肤/主题：背景 + 强调色（播放/暂停键）+ 文字色（歌词）+ 图标，见类顶部注释 ──────────────
    // 内置皮肤直接查 MobileSkinCatalog/MobileSkinIconCatalog；客制化主题得先从磁盘 Load 出
    // CustomTheme 再转成同一份 MobileSkinPalette（MobileSkinCatalog.FromCustomTheme），背景支持
    // 纯色/渐变——渐变这段跟桌面版 CustomThemeWindow.BuildBackgroundBrush、这个 Mobile 项目里
    // CustomThemePage.BuildBackgroundBrush 是同一个思路，这边是第三份（Uno 页面各自独立，见类顶部
    // "故意不共享状态"的注释，画布也是各自的），选中的自定义主题文件被删了就落回内置默认皮肤，
    // 不能让这个页面因为一个坏掉的主题直接失去配色。
    private void ApplyPalette()
    {
        string skinId = Droid.MobileSettingsStore.SelectedSkinId;
        Brush backgroundBrush;
        WriteableBitmap? icon = null;
        WriteableBitmap[]? animatedFrames = null;
        double frameDurationSeconds = 0;

        if (skinId.StartsWith(MobileSkinCatalog.CustomThemePrefix, StringComparison.Ordinal))
        {
            string fileName = skinId[MobileSkinCatalog.CustomThemePrefix.Length..];
            var theme = _customThemeStore?.Load(fileName);
            if (theme != null)
            {
                _palette = MobileSkinCatalog.FromCustomTheme(theme);
                backgroundBrush = BuildBackgroundBrush(theme.Background!);

                // 有 Frames 就只认 Frames、忽略 Rows（跟 FloatingOverlayService.ApplySkin 同一条
                // 规则），1 帧当静态图标，>1 帧 IconFrameTick 才会真的播动画
                if (theme.Icon != null)
                {
                    var iconPalette = CustomThemeValidator.BuildIconPalette(theme.Icon);
                    if (theme.Icon.Frames is { Count: > 0 } frames)
                    {
                        var frameRows = frames.Select(f => f.ToArray()).ToList();
                        var bitmaps = PixelIconRenderer.RenderFrames(frameRows, iconPalette);
                        icon = bitmaps[0];
                        if (bitmaps.Length > 1)
                        {
                            animatedFrames = bitmaps;
                            frameDurationSeconds = CustomThemeValidator.GetFrameDurationSeconds(theme.Icon);
                        }
                    }
                    else if (theme.Icon.Rows is { Count: > 0 } rows)
                    {
                        icon = PixelIconRenderer.Render(rows.ToArray(), iconPalette);
                    }
                }
            }
            else
            {
                _palette = MobileSkinCatalog.Find(null);
                backgroundBrush = new SolidColorBrush(ToUiColor(_palette.Background));
            }
        }
        else
        {
            // 内置皮肤目前都是单帧，见 MobileSkinIconCatalog 顶部注释——不会走到 animatedFrames 这条路
            _palette = MobileSkinCatalog.Find(skinId);
            backgroundBrush = new SolidColorBrush(ToUiColor(_palette.Background));
            if (MobileSkinIconCatalog.Find(skinId) is { } builtinIcon)
            {
                icon = PixelIconRenderer.Render(builtinIcon.Rows, builtinIcon.Palette);
            }
        }

        // 换了皮肤/主题，不管新皮肤有没有动画，先把上一套的帧动画状态清掉——不清的话切到一个没有
        // 动画的皮肤，IconFrameTick 还会拿着上一套主题的位图数组继续切，见 FloatingOverlayService
        // 里同名注释，这边是同一个坑
        _iconFrameBitmaps = animatedFrames;
        _iconFrameDurationSeconds = frameDurationSeconds;
        _iconFrameIndex = 0;
        _iconFrameElapsedMs = 0;

        RootGrid.Background = backgroundBrush;

        // 播放/暂停键：底色是皮肤强调色，图标颜色现算保证跟底色对比度够（PickReadableForeground）
        var readableOnAccent = RgbaColor.PickReadableForeground(_palette.Accent);
        var accentBrush = new SolidColorBrush(ToUiColor(_palette.Accent));
        BtnPlayPause.Background = accentBrush;
        BtnPlayPause.BorderBrush = accentBrush;
        _playIconBitmap = BuildIconBitmap(PlayIconRows, readableOnAccent);
        _pauseIconBitmap = BuildIconBitmap(PauseIconRows, readableOnAccent);
        IconPlayPause.Source = _isPlaying ? _pauseIconBitmap : _playIconBitmap;

        // 上一首/下一首：底色是强调色跟黑混一档调暗（不能跟播放键同样亮，那样三个键抢一样重、
        // 播放键就不突出了），图标颜色同样现算保证读得清
        var dimAccent = MixTowardBlack(_palette.Accent, 0.65);
        var readableOnDim = RgbaColor.PickReadableForeground(dimAccent);
        var dimAccentBrush = new SolidColorBrush(ToUiColor(dimAccent));
        BtnPrev.Background = dimAccentBrush;
        BtnPrev.BorderBrush = accentBrush;
        BtnNext.Background = dimAccentBrush;
        BtnNext.BorderBrush = accentBrush;
        IconPrev.Source = BuildIconBitmap(PrevIconRows, readableOnDim);
        IconNext.Source = BuildIconBitmap(NextIconRows, readableOnDim);

        if (icon != null)
        {
            SkinIcon.Source = icon;
            SkinIcon.Visibility = Visibility.Visible;
        }
        else
        {
            SkinIcon.Visibility = Visibility.Collapsed;
        }

        UpdateLyricUi(); // 换了颜色，正在显示的这句歌词（卡拉OK 两级色）要立刻跟着重画，不用等下一个 tick
    }

    /// <summary>把 PlayIconRows 这种 8x8 网格（'#' 是唯一颜色字符）配上一个颜色现画成位图——播放
    /// 控制三个按钮的图标都是这么来的，见类顶部注释为什么不用 emoji。</summary>
    private static WriteableBitmap BuildIconBitmap(string[] rows, RgbaColor color) =>
        PixelIconRenderer.Render(rows, new Dictionary<char, RgbaColor> { ['#'] = color }, scale: 4);

    /// <summary>往黑色混一档调暗——fraction 是"离黑色还有多远"（0 = 纯黑，1 = 原色不变），上一首/
    /// 下一首按钮底色用这个跟播放键的满强调色拉出亮度差，不是换个完全不同的色相。</summary>
    private static RgbaColor MixTowardBlack(RgbaColor c, double fraction) => new(
        c.A,
        (byte)(c.R * fraction),
        (byte)(c.G * fraction),
        (byte)(c.B * fraction));

    // 跟桌面版 CustomThemeWindow.BuildBackgroundBrush、CustomThemePage.BuildBackgroundBrush 同一个
    // 思路（纯色/渐变，渐变支持 vertical/diagonal 两个方向）——这是第三份，见本方法上面那段注释
    // 为什么不复用前两份
    private static Brush BuildBackgroundBrush(CustomThemeBackground bg)
    {
        var stops = (bg.Stops ?? new List<string>())
            .Select(s => { CustomThemeValidator.TryParseHexColor(s, out var c); return ToUiColor(c); })
            .ToList();

        if (string.Equals(bg.Type, "gradient", StringComparison.OrdinalIgnoreCase) && stops.Count >= 2)
        {
            var gradient = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = string.Equals(bg.Direction, "diagonal", StringComparison.OrdinalIgnoreCase)
                    ? new Windows.Foundation.Point(1, 1) : new Windows.Foundation.Point(0, 1),
            };
            for (int i = 0; i < stops.Count; i++)
            {
                gradient.GradientStops.Add(new GradientStop { Color = stops[i], Offset = stops.Count == 1 ? 0 : (double)i / (stops.Count - 1) });
            }
            return gradient;
        }
        return new SolidColorBrush(stops.Count > 0 ? stops[0] : Color.FromArgb(255, 0, 0, 0));
    }

    private static Color ToUiColor(RgbaColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    /// <summary>活跃会话列表变了（新 App 开始播/原来那个会话没了）——挑第一个当"现在显示这个"，
    /// 换绑 MediaController.Callback。同一个会话没变的话什么都不用做，回调本身会推更新过来。</summary>
    private void RebindController(IList<AndroidMediaSession.MediaController>? controllers)
    {
        var next = controllers != null && controllers.Count > 0 ? controllers[0] : null;
        bool sameSession = _controller != null && next != null && Equals(_controller.SessionToken, next.SessionToken);
        if (sameSession) return;

        if (_controller != null && _controllerCallback != null)
        {
            _controller.UnregisterCallback(_controllerCallback);
        }

        _controller = next;
        _controllerCallback = null;
        _currentTitle = "";
        _currentArtist = "";
        _totalDuration = TimeSpan.Zero;
        _fetchedForTrackKey = null;
        _currentLines = null;
        _currentLrcContent = null;
        _translationLines = null;
        _fetchCts?.Cancel();

        if (_controller == null)
        {
            UpdateLyricUi();
            return;
        }

        _controllerCallback = new PlaybackCallback(this);
        _controller.RegisterCallback(_controllerCallback, _mainHandler);

        // RegisterCallback 只保证"以后变化"会推过来，这个会话当前已经在报的状态得自己先问一次，
        // 不然要等下一次播放状态/元数据变化才会第一次显示出东西
        HandleMetadataChanged(_controller.Metadata);
        HandlePlaybackStateChanged(_controller.PlaybackState);
    }

    private void HandleMetadataChanged(MediaMetadata? metadata)
    {
        _currentTitle = metadata?.GetString(MediaMetadata.MetadataKeyTitle) ?? "（无标题）";
        _currentArtist = metadata?.GetString(MediaMetadata.MetadataKeyArtist) ?? "";
        long durationMs = metadata?.GetLong(MediaMetadata.MetadataKeyDuration) ?? 0;
        _totalDuration = durationMs > 0 ? TimeSpan.FromMilliseconds(durationMs) : TimeSpan.Zero;

        string trackKey = _currentTitle + "|" + _currentArtist;
        if (trackKey != _fetchedForTrackKey && !_fetchInFlight)
        {
            _fetchedForTrackKey = trackKey;
            _currentLines = null;
            _currentLrcContent = null;
            _translationLines = null;
            _fetchCts?.Cancel();
            _fetchCts = new CancellationTokenSource();
            _ = FetchLyricsAsync(_currentTitle, _currentArtist, _totalDuration, _fetchCts.Token);
        }

        UpdateLyricUi();
    }

    private void HandlePlaybackStateChanged(AndroidMediaSession.PlaybackState? state)
    {
        _anchorPosition = TimeSpan.FromMilliseconds(state?.Position ?? 0);
        _anchorTime = DateTimeOffset.Now;
        _isPlaying = state?.State == AndroidMediaSession.PlaybackStateCode.Playing;
        _playbackRate = (state?.PlaybackSpeed is float r && r > 0) ? r : 1.0;
        UpdateLyricUi();
    }

    private void HandleControllerSessionDestroyed()
    {
        if (_controller != null && _controllerCallback != null)
        {
            _controller.UnregisterCallback(_controllerCallback);
        }
        _controller = null;
        _controllerCallback = null;
        UpdateLyricUi();
    }

    private async Task FetchLyricsAsync(string title, string artist, TimeSpan expectedDuration, CancellationToken token)
    {
        _fetchInFlight = true;
        try
        {
            string? lrcContent = _lyricsCache?.TryGet(title + "|" + artist);
            if (lrcContent == null)
            {
                var result = await _lyricsFetcher!.FetchAsync(title, artist, expectedDuration, token);
                if (token.IsCancellationRequested) return; // 抓词这几秒里歌又换了，这份结果作废

                lrcContent = result?.Lrc;
                if (!string.IsNullOrEmpty(lrcContent)) _lyricsCache?.Save(title + "|" + artist, lrcContent);
            }

            _currentLrcContent = lrcContent;
            _currentLines = !string.IsNullOrEmpty(lrcContent)
                ? LrcParser.ParseLines(lrcContent)
                : new List<(int TimeMs, string Text)>(); // 空列表当"确实找不到"，跟"还没抓完"（null）区分开

            if (_bilingualEnabled) TryEnsureTranslationForCurrentTrack();
        }
        catch
        {
            // 联网失败/超时——继续显示"没找到歌词"就行，不需要弹错误吓用户
            _currentLines = new List<(int TimeMs, string Text)>();
        }
        finally
        {
            _fetchInFlight = false;
        }
        UpdateLyricUi();
    }

    private void TryEnsureTranslationForCurrentTrack()
    {
        if (string.IsNullOrEmpty(_currentLrcContent) || _translationLines != null) return;

        string trackKey = _currentTitle + "|" + _currentArtist;
        string? cachedTranslation = _lyricsCache?.TryGetTranslation(trackKey);
        if (cachedTranslation != null)
        {
            _translationLines = LrcParser.ParseLines(cachedTranslation);
            return;
        }

        _ = TranslateAsync(trackKey, _currentLrcContent, _fetchCts?.Token ?? CancellationToken.None);
    }

    private async Task TranslateAsync(string trackKey, string lrcContent, CancellationToken token)
    {
        try
        {
            string? translated = await _translator!.TranslateLrcAsync(lrcContent, token);
            if (token.IsCancellationRequested || translated == null) return;

            _lyricsCache?.SaveTranslation(trackKey, translated);
            if (trackKey == _currentTitle + "|" + _currentArtist)
            {
                _translationLines = LrcParser.ParseLines(translated);
                UpdateLyricUi();
            }
        }
        catch
        {
            // 翻译失败不影响原文正常显示，静默放弃就行
        }
    }

    /// <summary>跟 FloatingOverlayService.RenderTick 同一个思路：只做"插值算当前播放到哪了 + 找该
    /// 显示哪一行"这几步纯本地计算，不碰任何系统 API，自己用 Handler.PostDelayed 排下一次——见
    /// _mainHandler 字段那段注释，这个页面不用 DispatcherTimer 就是因为它在这个环境里不会自己反复
    /// 触发。</summary>
    private void RenderTick()
    {
        RetryBindControllerIfMissing();
        UpdateLyricUi();
        _mainHandler?.PostDelayed(_renderTickAction!, RenderIntervalMs);
    }

    // 悬浮窗那边踩过的坑同样搬过来防一下：页面刚打开那一刻 MediaNotificationListenerService.Instance
    // 恰好还没连接上，第一次 RebindController(null) 会把 _controller 定死成 null，之后只要活跃会话
    // 列表本身没有再变化过，就永远没有下一次机会重新查。这里顶个每隔几秒重试一次的兜底。
    private void RetryBindControllerIfMissing()
    {
        if (_controller != null) return;
        if ((DateTimeOffset.Now - _lastControllerRebindAttempt).TotalMilliseconds < ControllerRebindRetryIntervalMs) return;

        _lastControllerRebindAttempt = DateTimeOffset.Now;
        RebindController(Droid.MediaNotificationListenerService.Instance?.GetActiveMediaControllers());
    }

    /// <summary>客制化主题 icon.frames 的逐帧播放——跟 FloatingOverlayService.IconFrameTick 同一个
    /// "tick 累计时间、攒够一帧的时长才切"思路，_iconFrameBitmaps 为 null 或者只有 1 张（静态图标）
    /// 就什么都不做，但这个 tick 本身照样一直排下去，不然下次 ApplyPalette 切到一个真的有动画的主题
    /// 时还得再额外想办法把它重新启动一遍。</summary>
    private void IconFrameTick()
    {
        var frames = _iconFrameBitmaps;
        if (frames is { Length: > 1 })
        {
            _iconFrameElapsedMs += IconFrameTickIntervalMs;
            double frameDurationMs = Math.Max(50, _iconFrameDurationSeconds * 1000); // 下限保护，见 FloatingOverlayService 同名注释
            if (_iconFrameElapsedMs >= frameDurationMs)
            {
                _iconFrameElapsedMs = 0;
                _iconFrameIndex = (_iconFrameIndex + 1) % frames.Length;
                SkinIcon.Source = frames[_iconFrameIndex];
            }
        }

        _mainHandler?.PostDelayed(_iconFrameTickAction!, IconFrameTickIntervalMs);
    }

    /// <summary>无操作 IdleHideSeconds 秒后把顶部条（退出按钮/图标/曲名）隐藏——这个页面本来就是给
    /// 人盯着看歌词的，顶部条一直摆着反而分心，见类顶部注释。这是一次性的 PostDelayed，不是重复
    /// 排程，每次真的有交互都靠 ShowTopBarAndResetIdleTimer 先 RemoveCallbacks 掉上一次排的、
    /// 再重新 PostDelayed 一遍，从头计时，不是接着上次剩下的时间继续算。</summary>
    private void HideTopBar()
    {
        TopBar.Visibility = Visibility.Collapsed;
    }

    /// <summary>点了屏幕任意位置/按了任意播放控制按钮都算一次"交互"——顶部条（如果已经隐藏）先露出来，
    /// 再重新掐表计时，不管当前隐藏/显示状态如何，退出全屏这个按钮本身也在顶部条里，藏起来了得先
    /// 点一下屏幕唤出才点得到，硬件返回键不受影响（见 MainActivity.OnBackPressed，走的是另一条路）。</summary>
    private void ShowTopBarAndResetIdleTimer()
    {
        TopBar.Visibility = Visibility.Visible;
        if (_hideTopBarAction == null || _mainHandler == null) return;
        _mainHandler.RemoveCallbacks(_hideTopBarAction);
        _mainHandler.PostDelayed(_hideTopBarAction, IdleHideSeconds * 1000);
    }

    private void UpdateLyricUi()
    {
        IconPlayPause.Source = _isPlaying ? _pauseIconBitmap : _playIconBitmap;

        if (_controller == null)
        {
            TxtTitle.Text = "ZipPlay";
            TxtArtist.Text = "（现在没有检测到正在播放的 App）";
            SetLyricPlain("");
            TxtTranslationLine.Text = "";
            TxtProgress.Text = "";
            ProgressSlider.Visibility = Visibility.Collapsed;
            return;
        }

        TxtTitle.Text = _currentTitle;
        TxtArtist.Text = _currentArtist;

        if (_currentLines == null)
        {
            SetLyricPlain(_fetchInFlight ? "（正在联网找歌词…）" : "（没找到歌词）");
            TxtTranslationLine.Text = "";
            TxtProgress.Text = "";
            ProgressSlider.Visibility = Visibility.Collapsed;
            return;
        }

        var position = PlaybackPositionEstimator.Estimate(
            _anchorPosition, _anchorTime, DateTimeOffset.Now, _isPlaying, _playbackRate, _totalDuration);
        int positionMs = (int)position.TotalMilliseconds + _syncOffsetMs;
        TxtProgress.Text = FormatProgress(position, _totalDuration);

        // 不知道总时长就没法拖出个有意义的比例，直接隐藏，不摆一条拖了也没用的进度条；正在拖的时候
        // 不能反过来把 Value 拽回真实播放位置，不然手指没松开进度条自己跳回去，根本拖不动，见
        // ProgressSlider_PointerPressed/Released
        if (_totalDuration > TimeSpan.Zero)
        {
            ProgressSlider.Visibility = Visibility.Visible;
            ProgressSlider.Maximum = _totalDuration.TotalMilliseconds;
            if (!_isDraggingSeek) ProgressSlider.Value = position.TotalMilliseconds;
        }
        else
        {
            ProgressSlider.Visibility = Visibility.Collapsed;
        }

        int currentIndex = -1;
        for (int i = 0; i < _currentLines.Count; i++)
        {
            if (_currentLines[i].TimeMs > positionMs) break;
            currentIndex = i;
        }

        if (currentIndex < 0)
        {
            SetLyricPlain("");
            TxtTranslationLine.Text = "";
            return;
        }

        var (lineStartMs, lineText) = _currentLines[currentIndex];
        int lineEndMs = currentIndex + 1 < _currentLines.Count
            ? _currentLines[currentIndex + 1].TimeMs
            : (_totalDuration > TimeSpan.Zero ? (int)_totalDuration.TotalMilliseconds : lineStartMs + 4000);
        int sungChars = KaraokeTiming.EstimateSungChars(lineText, positionMs, lineStartMs, lineEndMs);

        string? translationText = null;
        if (_translationLines != null)
        {
            foreach (var (timeMs, text) in _translationLines)
            {
                if (timeMs > positionMs) break;
                translationText = text;
            }
        }

        SetLyricKaraoke(lineText, sungChars);
        TxtTranslationLine.Text = translationText ?? "";
    }

    // "3:07 / 4:52"——总时长拿不到（系统那首歌没报，比如刚切歌那一瞬间）就只显示已播位置，不硬凑
    // 一个 "/ 0:00" 出来
    private static string FormatProgress(TimeSpan position, TimeSpan total)
    {
        string pos = $"{(int)position.TotalMinutes}:{position.Seconds:D2}";
        if (total <= TimeSpan.Zero) return pos;
        return $"{pos} / {(int)total.TotalMinutes}:{total.Seconds:D2}";
    }

    /// <summary>字号按这一行的字符数分几档往下降——默认 30px 是给"正常长度"这句歌词配的，很长的一句
    /// （长歌词/双语原文比较常见）不缩小的话会被 TextWrapping 挤成好几行甚至看不全，缩小字号比硬
    /// 靠自动换行更好读。档位是拍脑袋定的经验值，没有精确公式，够用就行。</summary>
    private static double LyricFontSizeFor(string text) => text.Length switch
    {
        <= 18 => 30,
        <= 30 => 24,
        <= 45 => 20,
        _ => 16,
    };

    private void SetLyricPlain(string text)
    {
        TxtLyricLine.FontSize = LyricFontSizeFor(text);
        TxtLyricLine.Inlines.Clear();
        TxtLyricLine.Inlines.Add(new Run { Text = text, Foreground = new SolidColorBrush(ToUiColor(_palette.Text)) });
    }

    /// <summary>逐字上色：唱过的部分用当前皮肤的文字色，还没唱到的部分把 alpha 减半调暗——跟悬浮窗
    /// SetOverlayLyricLine 同一个"两级颜色"精简做法，颜色数据源也是同一份 MobileSkinPalette.Text，
    /// 只是这边是两个 Run 不是 Android 那边的 SpannableString。开了卡拉OK 且真的唱到一半（不是 0
    /// 也不是全唱完）才分两段上色，否则整行同一个颜色，免得一行歌词刚出现就有一半显示成"暗的"，
    /// 看起来像卡住了。</summary>
    private void SetLyricKaraoke(string lineText, int sungChars)
    {
        TxtLyricLine.FontSize = LyricFontSizeFor(lineText);
        TxtLyricLine.Inlines.Clear();
        var sungColor = new SolidColorBrush(ToUiColor(_palette.Text));
        if (_karaokeEnabled && sungChars > 0 && sungChars < lineText.Length)
        {
            var dimColor = new SolidColorBrush(Color.FromArgb((byte)(_palette.Text.A / 2), _palette.Text.R, _palette.Text.G, _palette.Text.B));
            TxtLyricLine.Inlines.Add(new Run { Text = lineText[..sungChars], Foreground = sungColor });
            TxtLyricLine.Inlines.Add(new Run { Text = lineText[sungChars..], Foreground = dimColor });
        }
        else
        {
            TxtLyricLine.Inlines.Add(new Run { Text = lineText, Foreground = sungColor });
        }
    }

    /// <summary>MediaSessionManager.OnActiveSessionsChangedListener 这个 Java 接口的最简单实现——
    /// 就一个方法，包一层委托转发给外面，不用为了实现它专门去继承什么，见 FloatingOverlayService
    /// 里同名的类（这边故意重新写一份小的，不是共享同一个类型，见本文件类顶部注释）。</summary>
    private sealed class ActiveSessionsListener : Java.Lang.Object, AndroidMediaSession.MediaSessionManager.IOnActiveSessionsChangedListener
    {
        private readonly Action<IList<AndroidMediaSession.MediaController>?> _onChanged;

        public ActiveSessionsListener(Action<IList<AndroidMediaSession.MediaController>?> onChanged) => _onChanged = onChanged;

        public void OnActiveSessionsChanged(IList<AndroidMediaSession.MediaController>? controllers) => _onChanged(controllers);
    }

    /// <summary>MediaController.Callback 是个抽象类不是接口，得真的继承——播放状态变化（暂停/继续/
    /// seek/倍速）和元数据变化（换歌）各转发给外面一个方法，会话彻底没了也转发一下。</summary>
    private sealed class PlaybackCallback : AndroidMediaSession.MediaController.Callback
    {
        private readonly FullScreenPlayerPage _owner;

        public PlaybackCallback(FullScreenPlayerPage owner) => _owner = owner;

        public override void OnPlaybackStateChanged(AndroidMediaSession.PlaybackState? state) => _owner.HandlePlaybackStateChanged(state);

        public override void OnMetadataChanged(MediaMetadata? metadata) => _owner.HandleMetadataChanged(metadata);

        public override void OnSessionDestroyed() => _owner.HandleControllerSessionDestroyed();
    }

    /// <summary>SharedPreferences.OnSharedPreferenceChangeListener 的最简单实现——同一个套路，
    /// 就一个方法，包一层委托转发给外面，见 FloatingOverlayService 里同名的类（这边故意重新写一份
    /// 小的，不是共享同一个类型，见本文件类顶部注释）。key 是哪个设置项变了这里用不上，皮肤/偏移/
    /// 卡拉OK/双语哪个设置一变就整个重新读一遍，见 StartSession 里注册的那个回调。</summary>
    private sealed class SettingsPrefListener : Java.Lang.Object, ISharedPreferencesOnSharedPreferenceChangeListener
    {
        private readonly Action _onChanged;

        public SettingsPrefListener(Action onChanged) => _onChanged = onChanged;

        public void OnSharedPreferenceChanged(ISharedPreferences? sharedPreferences, string? key) => _onChanged();
    }
#endif
}
