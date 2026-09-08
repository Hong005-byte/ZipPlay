using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using Windows.UI;
using PixelLyric8BitFix;

#if __ANDROID__
using Android.Content;
using Android.Media;
using Android.OS;
using Android.Views;
using Android.Widget;
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

    /// <summary>动感歌词的顶部条快捷开关——不用退出全屏、跑去「歌词功能设置」页才能切换，直接在这
    /// 翻转同一个设置项（MobileSettingsStore.KineticLyricsEnabled）。翻转之后 UI 怎么跟着变见
    /// StartSession 里注册的那个 SettingsPrefListener——那边订阅的是"任意设置变了"，不是只订阅
    /// 那个设置页自己改的情况，这里直接改 SharedPreferences 也会触发同一条回调，_kineticEnabled
    /// 这个字段和显示会自动跟着刷新，不用在这个方法里重复一遍那些逻辑。</summary>
    private void BtnToggleKinetic_Click(object sender, RoutedEventArgs e)
    {
#if __ANDROID__
        ShowTopBarAndResetIdleTimer();
        Droid.MobileSettingsStore.KineticLyricsEnabled = !Droid.MobileSettingsStore.KineticLyricsEnabled;
#endif
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

    // 手动导入 LRC——只在确认联网找不到词的时候才露出来（见 UpdateLyricUi 里 BtnImportLrc.Visibility
    // 那段），方法签名照样得留在 #if __ANDROID__ 外面，理由跟其它 Click 方法一样：XAML 里的
    // Click="BtnImportLrc_Click" 不分平台，编译到非 Android TFM 也要能找到同名方法
    private void BtnImportLrc_Click(object sender, RoutedEventArgs e)
    {
#if __ANDROID__
        ShowTopBarAndResetIdleTimer();
        _ = ImportLrcFromFileAsync();
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
    private bool _kineticEnabled;
    private int _syncOffsetMs;
    private DateTimeOffset _lastControllerRebindAttempt = DateTimeOffset.MinValue;

    // ── 动感歌词（KineticLyricsEnabled），见 XAML 里 KineticLyricHost 那段注释和 UpdateKineticLyric ──
    // 按几个词一组切出来的"段" + 各自的估算起止时间（同一行只切一次，见 _kineticWordsForLineKey
    // 判断换行）。_kineticRevealedCount 是这一行已经冒出来、还留在屏幕上的段数——只增不减，换行
    //才清零，见 UpdateKineticLyric 顶部注释这版"段不会被顶掉，攒到换行才清空"的取舍。
    // _kineticSegmentViews 是当前留在 KineticLyricHost 里的那些动态建出来的 TextBlock，方便换行时
    // 一次性都摘掉，不用挨个找。
    private (string Text, int StartMs, int EndMs)[]? _kineticWords;
    private string? _kineticWordsForLineKey;
    private int _kineticRevealedCount;
    private readonly List<TextBlock> _kineticSegmentViews = new();
    private const int KineticWordsPerSegment = 2; // 几个词分一组当一"段"，见 SplitLineIntoSegmentsWithTiming

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

    // ── 客制化主题的多层装饰（layers，最多 2 个），见 XAML 里 LayerIcon0/LayerIcon1 那段注释——
    // 贴在整个全屏页面四角之一（用户要的"右下角放主题自己的图标"就是 anchor=bottom-right 这一种），
    // 跟主图标共用同一个"tick 累计时间、攒够一帧才切"逻辑，抽成 ImageFrameState 复用，见该类定义，
    // 挂在 IconFrameTick 那个 tick 上一起播，不单开一份 ─────────────────────────────────
    private readonly ImageFrameState _layer0Anim = new();
    private readonly ImageFrameState _layer1Anim = new();

    // ── 跟着音乐跳动的律动条，见 XAML 里 MusicBarsCanvas 那段注释和 UpdateMusicBars ────────────
    // 柱子数量/宽度/边距/每根柱子的摆动频率&相位都是拍脑袋的经验值，够用就行，不是精确设计。间距
    // （不是这里的常量）按 MusicBarsCanvas.ActualWidth 现场撑满两侧边距之间的整段宽度，见
    // UpdateMusicBars 顶部注释——横屏这种宽画布也能铺满，不会只占中间一小条。纵向峰值封顶在
    // MusicBarMaxHeightCap 这个固定值（同时不超过画布高度的一个比例，兜底极端窄画布），不按屏幕
    // 高度等比拉伸——竖屏这种细长比例不该被拉到接近顶部条/底部控制条。
    private const int MusicBarCount = 16;
    private const double MusicBarWidth = 18; // 见反馈"律动条看起来太细了"，从 10 加粗到 18
    private const double MusicBarSideMargin = 24;
    private const double MusicBarMaxHeightCap = 260;
    private const double MusicBarMinHeight = 6;
    private const int MusicBarsTickIntervalMs = 80;
    private Microsoft.UI.Xaml.Shapes.Rectangle[]? _musicBarRects;
    private double _musicBarsElapsedMs;
    private Action? _musicBarsTickAction;

    // 每根柱子各自的摆动频率/相位——纯拍脑袋的经验值，让这些柱子的节奏各不相同，叠加起来看着像
    // 真的在跳动而不是整齐划一的机械摆动。跟悬浮窗 IconMotionState 那几种"简单招式"是同一个"没有
    // 真频谱分析就用手工参数模拟节奏感"的思路，见 AudioReactiveCapture 类顶部注释——这台设备上能
    // 拿到的音乐律动数据只有"整体响度→速度倍率"这一个标量（RawRatio），不是逐频段的频谱，没法做到
    // "每根柱子对应一个真实频段"那种效果，这版退而求其次：所有柱子共享同一份"响度→速度"信号（跟
    // 皮肤律动动画同一个数据源），各自用不同的固定频率/相位错开节奏，见 UpdateMusicBars
    private static readonly double[] MusicBarFrequencies =
        { 1.7, 2.3, 1.3, 2.9, 1.1, 2.6, 1.9, 2.1, 1.5, 2.8, 1.2, 2.4, 1.8, 3.0, 1.4, 2.0 };
    private static readonly double[] MusicBarPhases =
        { 0.0, 0.8, 1.6, 0.3, 2.4, 1.1, 0.5, 1.9, 2.7, 0.6, 1.4, 2.2, 0.2, 1.0, 1.7, 2.5 };

    // ── 无操作几秒顶部条自动隐藏，见 HideTopBar/ShowTopBarAndResetIdleTimer ───────────────────────
    private const int IdleHideSeconds = 4;

    // ── 播放进度条拖动，见 ProgressSlider_PointerPressed/Released——拖着的时候 UpdateLyricUi 不能
    // 顺手把 Value 拽回真实播放位置，不然手指还没松开进度条就自己跳回去，根本拖不动 ───────────────
    private bool _isDraggingSeek;

    // ── 播放控制三个按钮的像素图标（8x8，'#' 是唯一用到的颜色字符，具体什么颜色由 ApplyPalette
    // 现算），见类顶部注释为什么不用 emoji。SkipNext 是"右指三角"思路，Prev 是 SkipNext 整行反过来
    // （见 CustomThemePage 同类注释里"闭包坑"那种手动核对，这边是几何镜像，已经手动验证过：原图案
    // [0,W-1] 反过来变成 [8-W,7]，右指三角镜像后变成左指三角）──────────────────────────────
    //
    // Play 单独一份、不跟 SkipNext 共用同一个三角形状：SkipNext 的三角形只占左半个格子（宽度封顶在
    // 4/8），右边留出的空档是留给旁边那道"跳过"竖杠用的，两者拼在一起才是完整的 8 宽图案；但播放键
    // 是唯一一个不用跟别的图形拼格子的按钮，继续用那个"半宽三角"的话会显得又瘦又偏左——三角形实际
    // 占的像素全挤在 8x8 画布的左半边，右半边永远是空的，缩放到按钮里之后看起来这颗三角形离按钮中心
    // 是偏的，不是正中间，宽高比也显瘦。这份改成顶格到 col6（只留 col7 一列边距），每两行宽度 +2
    // （1/3/5/7/7/5/3/1，中间两行封顶）——三角形占满了差不多整个画布宽度，缩放到按钮里视觉上居中，
    // 也不再显得单薄。
    private static readonly string[] PlayIconRows =
    {
        "#.......", "###.....", "#####...", "#######.",
        "#######.", "#####...", "###.....", "#.......",
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
        _kineticEnabled = Droid.MobileSettingsStore.KineticLyricsEnabled;
        _syncOffsetMs = Droid.MobileSettingsStore.SyncOffsetMs;

        // 律动条的柱子必须先建出来再调 ApplyPalette——ApplyPalette 里给柱子上色那段是"_musicBarRects
        // != null 才上色"，先后顺序反了的话，_musicBarRects 这一刻还是 null，第一次上色直接被跳过，
        // 柱子会一直顶着刚 new 出来时的默认 Fill（透明）留到下一次皮肤真的被换掉才会第一次显示颜色——
        // 真机测出来的坑：柱子位置/高度都算对了，就是啥都看不见，以为整个功能没生效，其实是没上色。
        BuildMusicBars();
        ApplyPalette();

        // 皮肤在设置页被换掉（哪怕这个全屏页正开着）也要跟着立刻换，见类顶部注释——跟悬浮窗
        // RefreshSettingsFromStore 是同一个用意，这边顺手把同步偏移/卡拉OK/双语这几个设置也一起
        // 重新读一遍，免得再单独订阅一遍
        _settingsPrefListener = new SettingsPrefListener(() =>
        {
            _karaokeEnabled = Droid.MobileSettingsStore.KaraokeEnabled;
            _bilingualEnabled = Droid.MobileSettingsStore.BilingualEnabled;
            _kineticEnabled = Droid.MobileSettingsStore.KineticLyricsEnabled;
            _syncOffsetMs = Droid.MobileSettingsStore.SyncOffsetMs;
            ApplyPalette();
        });
        Droid.MobileSettingsStore.RegisterChangeListener(_settingsPrefListener);

        _mainHandler = new Handler(Looper.MainLooper!);

        _sessionManager = (AndroidMediaSession.MediaSessionManager)context.GetSystemService(Context.MediaSessionService)!;
        _activeSessionsListener = new ActiveSessionsListener(RebindController);
        TryRegisterActiveSessionsListener(); // 见该方法注释——没拿到通知使用权就直接调这个会抛 SecurityException
        RebindController(Droid.MediaNotificationListenerService.Instance?.GetActiveMediaControllers());

        _renderTickAction = RenderTick;
        _mainHandler.Post(_renderTickAction); // 立刻先画一次，不用等第一个 tick 才有内容，之后 RenderTick 自己 PostDelayed 排下一次

        _iconFrameTickAction = IconFrameTick;
        _mainHandler.Post(_iconFrameTickAction);

        _musicBarsTickAction = MusicBarsTick;
        _mainHandler.Post(_musicBarsTickAction);

        _hideTopBarAction = HideTopBar;
        ShowTopBarAndResetIdleTimer(); // 刚打开这一刻顶部条先露出来，不是一进来就是隐藏的
    }

    private bool _activeSessionsListenerRegistered;

    /// <summary>真正订阅"活跃会话列表变化"广播——这一步要求调用方已经拿到"通知使用权"（见
    /// PermissionsPage），没拿到的话系统会直接抛 SecurityException（不是返回 false/null 这种能提前
    /// 判断的信号）。真机踩过的坑：用户先进了这个全屏播放页、还没去开通知使用权，这个异常会一路冒出
    /// OnNavigatedTo 没人接，把整个 App 进程带崩，用户体感就是"点了全屏播放、App 卡死出不来"，跟
    /// FloatingOverlayService.TryRegisterActiveSessionsListener 是同一个坑、同一份修法。这里先查一遍
    /// 权限、真的抛了也接住，全屏页照样能正常显示（只是暂时读不到"现在在播什么"），不会因为这一步
    /// 崩掉整个页面。RetryBindControllerIfMissing 每隔几秒会再调一次这个方法，用户去设置里把通知
    /// 使用权补上之后，不用重新进一次这个页面，自己就能重新连上。</summary>
    private void TryRegisterActiveSessionsListener()
    {
        if (_activeSessionsListenerRegistered || _sessionManager == null || _activeSessionsListener == null) return;
        var context = global::Android.App.Application.Context;
        if (!Droid.MediaNotificationListenerService.IsListenerAccessGranted(context)) return;

        try
        {
            var component = new ComponentName(context, Java.Lang.Class.FromType(typeof(Droid.MediaNotificationListenerService)));
            _sessionManager.AddOnActiveSessionsChangedListener(_activeSessionsListener, component);
            _activeSessionsListenerRegistered = true;
        }
        catch (Exception ex)
        {
            // 权限查询和系统实际校验之间偶尔会有一拍时间差（刚开权限那一瞬间）——接住就行，下一次
            // RetryBindControllerIfMissing 的 tick 会再试一次，不影响这个页面本身正常显示
            global::Android.Util.Log.Warn("ZipPlayFullScreen", "AddOnActiveSessionsChangedListener failed: " + ex);
        }
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
        _musicBarsTickAction = null;

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
        _activeSessionsListenerRegistered = false;

        if (_controller != null && _controllerCallback != null)
        {
            _controller.UnregisterCallback(_controllerCallback);
        }
        _controller = null;
        _controllerCallback = null;

        _fetchCts?.Cancel();
        _currentLines = null;
        _translationLines = null;

        // 动感歌词攒着的那几段也一起收掉——这个页面目前每次进来都是新实例（没设
        // NavigationCacheMode），理论上不清也不会串场，但退出时把状态清干净跟上面那几行是同一个
        // 道理，以后万一改成缓存页面实例也不会带着上一次的残留段进来
        ClearKineticSegments();
        _kineticWords = null;
        _kineticWordsForLineKey = null;
        _kineticRevealedCount = 0;
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

        // 客制化主题的多层装饰（layers，最多 2 个）——只有客制化主题才有，内置皮肤没有这两样，见
        // XAML 里 LayerIcon0/LayerIcon1 那段注释和 CustomTheme.Layers 的字段注释
        WriteableBitmap[]? layer0Frames = null; double layer0Duration = 0; string? layer0Anchor = null;
        WriteableBitmap[]? layer1Frames = null; double layer1Duration = 0; string? layer1Anchor = null;

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

                // 用户要的"全屏模式右下角放主题自己的图标"就是这——layers[i].Anchor 填 "bottom-right"
                // 就会贴到屏幕右下角，见 ApplyFullScreenLayerAnchor。跟悬浮窗 FloatingOverlayService.
                // ApplySkin 里 layer0/layer1 那两段是同一份数据源，这边只吃 Icon（frames/rows），暂时
                // 不接 layer.Animation 那 6 种"简单招式"（pulse/sway 等）——那是另一层单独的效果，
                // 这次先把"贴在角上的图标本身能不能动"这个更直接的诉求做完
                if (theme.Layers is { Count: > 0 } layers)
                {
                    if (layers.Count > 0 && layers[0].Icon != null)
                    {
                        (layer0Frames, layer0Duration) = RenderThemeIconFrames(layers[0].Icon!);
                        layer0Anchor = layers[0].Anchor;
                    }
                    if (layers.Count > 1 && layers[1].Icon != null)
                    {
                        (layer1Frames, layer1Duration) = RenderThemeIconFrames(layers[1].Icon!);
                        layer1Anchor = layers[1].Anchor;
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

        // 顶部条那个小图标 + 右下角那个大图标是同一份数据的两个显示位置（见 XAML 里 CornerSkinIcon
        // 那段注释：用户要的"右下角放主题自己的 icon"指的就是这个主图标，不是可选的 layers 装饰层），
        // 逐帧动画也是同一份，见 IconFrameTick 里两个一起换 Source 那两行
        if (icon != null)
        {
            SkinIcon.Source = icon;
            SkinIcon.Visibility = Visibility.Visible;
            CornerSkinIcon.Source = icon;
            CornerSkinIcon.Visibility = Visibility.Visible;
        }
        else
        {
            SkinIcon.Visibility = Visibility.Collapsed;
            CornerSkinIcon.Visibility = Visibility.Collapsed;
        }

        // 多层装饰——换了皮肤/主题，不管新主题有没有配 layers，都要重新走一遍：换成一个没有 layers
        // 的主题/内置皮肤时，这俩局部变量本来就是 null，Reset/ApplyFullScreenLayerAnchor 会把上一套
        // 主题留下的图标清掉、Collapsed 隐藏，不会出现"切了皮肤，右下角还留着上一个主题的图标"
        _layer0Anim.Reset(LayerIcon0, layer0Frames, layer0Duration);
        _layer1Anim.Reset(LayerIcon1, layer1Frames, layer1Duration);
        ApplyFullScreenLayerAnchor(LayerIcon0, layer0Frames, layer0Anchor);
        ApplyFullScreenLayerAnchor(LayerIcon1, layer1Frames, layer1Anchor);

        // 律动条颜色跟着强调色走（跟播放键同一个颜色语义），透明度收得比较低——这是背景装饰，不该
        // 跟前景的歌词/播放键抢视觉重量，见这次反馈"律动会抢到歌词的呈现"，连同 XAML 里
        // MusicBarsDimOverlay 那层蒙版一起往暗了调
        if (_musicBarRects != null)
        {
            var barBrush = new SolidColorBrush(Color.FromArgb(
                (byte)(_palette.Accent.A * 0.55), _palette.Accent.R, _palette.Accent.G, _palette.Accent.B));
            foreach (var rect in _musicBarRects) rect.Fill = barBrush;
        }

        UpdateLyricUi(); // 换了颜色，正在显示的这句歌词（卡拉OK 两级色）要立刻跟着重画，不用等下一个 tick
    }

    /// <summary>一份 CustomThemeIcon（某个装饰层）转成预渲染好的帧数组——有 Frames 就只认 Frames、
    /// 忽略 Rows（跟主图标那段、FloatingOverlayService.RenderIconFrames 同一条规则），1 帧当静态图，
    /// >1 帧真的要循环播放；没有 Frames 才退回 Rows 当唯一一帧，两者都没有给 null，调用方
    /// （ApplyFullScreenLayerAnchor）决定"没有图标"该怎么办（隐藏这个 Image）。</summary>
    private static (WriteableBitmap[]? Frames, double DurationSeconds) RenderThemeIconFrames(CustomThemeIcon icon)
    {
        var palette = CustomThemeValidator.BuildIconPalette(icon);
        if (icon.Frames is { Count: > 0 } frames)
        {
            var frameRows = frames.Select(f => f.ToArray()).ToList();
            return (PixelIconRenderer.RenderFrames(frameRows, palette), CustomThemeValidator.GetFrameDurationSeconds(icon));
        }
        if (icon.Rows is { Count: > 0 } rows)
        {
            return (new[] { PixelIconRenderer.Render(rows.ToArray(), palette) }, 0);
        }
        return (null, 0);
    }

    /// <summary>按 layer.anchor（四个角之一）把这个装饰层摆到整个全屏页面的对应角上——用
    /// HorizontalAlignment/VerticalAlignment 而不是 Canvas.Left/Top 那种绝对坐标，是因为这俩 Image
    /// 挂在 RootGrid 上（Grid.RowSpan="3"，见 XAML），Grid 天然支持用对齐方式贴边，不用自己算屏幕
    /// 尺寸。没有图标/没配 anchor 就整个 Collapsed，跟 FloatingOverlayService.ApplyLayerAnchor 是
    /// 同一条规则（那边是 Android 原生 View 的 Gravity，这边是 XAML 的 Alignment，效果等价）。</summary>
    private static void ApplyFullScreenLayerAnchor(Microsoft.UI.Xaml.Controls.Image image, WriteableBitmap[]? frames, string? anchor)
    {
        if (frames is not { Length: > 0 } || anchor == null)
        {
            image.Visibility = Visibility.Collapsed;
            return;
        }
        image.Visibility = Visibility.Visible;
        switch (anchor)
        {
            case "top-right":
                image.HorizontalAlignment = HorizontalAlignment.Right;
                image.VerticalAlignment = VerticalAlignment.Top;
                break;
            case "bottom-left":
                image.HorizontalAlignment = HorizontalAlignment.Left;
                image.VerticalAlignment = VerticalAlignment.Bottom;
                break;
            case "bottom-right":
                image.HorizontalAlignment = HorizontalAlignment.Right;
                image.VerticalAlignment = VerticalAlignment.Bottom;
                break;
            default: // "top-left" 以及任何理论上不该出现的值（校验已经拦过），都落回左上角
                image.HorizontalAlignment = HorizontalAlignment.Left;
                image.VerticalAlignment = VerticalAlignment.Top;
                break;
        }
    }

    /// <summary>建好固定几根 Rectangle 当律动条的柱子——只在 StartSession 调一次，之后每个 tick
    /// （UpdateMusicBars）只改现成柱子的 Width/Height/Canvas.Left/Top，不重新创建元素。这里不摆
    /// 位置——MusicBarsCanvas 现在铺满整个屏幕（见 XAML），这一刻它的 ActualWidth/ActualHeight 还
    /// 没跑完布局，量出来大概率是 0，摆了也是错的，真正的位置留给 UpdateMusicBars 每个 tick 现算，
    /// 见该方法注释。柱子颜色由 ApplyPalette 现算（跟着皮肤强调色走），这里先给个占位（Fill 留空，
    /// 第一次 ApplyPalette 会立刻补上）。</summary>
    private void BuildMusicBars()
    {
        MusicBarsCanvas.Children.Clear();
        _musicBarRects = new Microsoft.UI.Xaml.Shapes.Rectangle[MusicBarCount];
        for (int i = 0; i < MusicBarCount; i++)
        {
            var rect = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = MusicBarWidth,
                Height = MusicBarMinHeight,
                RadiusX = MusicBarWidth / 2,
                RadiusY = MusicBarWidth / 2,
            };
            MusicBarsCanvas.Children.Add(rect);
            _musicBarRects[i] = rect;
        }
    }

    /// <summary>律动条自己的 tick——跟 IconFrameTick 分开一份（80ms，比 IconFrameTick 的 100ms 更密
    /// 一点，柱子连续摆动比逐帧图标切换更需要顺滑），见 StartSession/StopSession 里跟另外几个 tick
    /// 同一套"挂在 _mainHandler 上、PostDelayed 自己排下一次"的写法。</summary>
    private void MusicBarsTick()
    {
        UpdateMusicBars();
        _mainHandler?.PostDelayed(_musicBarsTickAction!, MusicBarsTickIntervalMs);
    }

    /// <summary>没有检测到正在播放的 App——律动条（连同灰色蒙版）整个隐藏，不摆一排没有归属的柱子在
    /// 那背景里。有 App 在播但暂停了：柱子留着（当纯背景装饰用，不是"没意义的东西"），只是时间累加
    /// 停在原地不再往前走，画面定格，不会在暂停的时候还自己一直跳。真的在播的话，每根柱子按自己的
    /// 固定频率/相位算一个三角波高度，叠加一个"响度→跳动幅度"的倍率——开了皮肤音乐律动且真的采集到
    /// 数据（AudioReactiveCapture.IsActive）才会让这个倍率跟着响度实时变化，否则退回一个固定的中等
    /// 倍率，见 MusicBarFrequencies/MusicBarPhases 那段注释和 AudioReactiveCapture 类顶部注释这条
    /// 数据链路本身的能力上限（只有整体响度，没有逐频段频谱）。
    ///
    /// 横向：柱子间距按 MusicBarsCanvas.ActualWidth 现场撑满整个宽度（不是固定像素总宽度居中摆），
    /// 见这次反馈——横屏时画布宽度比竖屏宽出一大截，之前那版横向总宽是"柱子宽+间距"算出来的一个
    /// 固定像素数，横屏下就只占屏幕中间一小条，两边大片空白，明显不是"满的"；改成宽度不够就撑大
    /// 间距填满，不管横屏竖屏都能顶到两侧留白（MusicBarSideMargin）为止。
    ///
    /// 纵向：柱子峰值封顶在一个不太高的固定值（MusicBarMaxHeightCap），不按屏幕高度的比例走——见
    /// 这次反馈"竖屏时候不要上到下全满"：上一版按 ActualHeight 的 92% 算峰值，在竖屏这种细长比例
    /// 的屏幕上会顶到接近顶部条/底部控制条，效果过头了；这版退回一个固定上限（同时留着
    /// canvasHeight 兜底，避免极端窄的画布，比如分屏模式下把柱子挤得比画布还高）。</summary>
    private void UpdateMusicBars()
    {
        if (_musicBarRects == null) return;

        if (_controller == null)
        {
            MusicBarsCanvas.Visibility = Visibility.Collapsed;
            MusicBarsDimOverlay.Visibility = Visibility.Collapsed;
            return;
        }
        MusicBarsCanvas.Visibility = Visibility.Visible;
        MusicBarsDimOverlay.Visibility = Visibility.Visible;

        if (_isPlaying) _musicBarsElapsedMs += MusicBarsTickIntervalMs; // 暂停就定格，不清零也不继续走
        double t = _musicBarsElapsedMs / 1000.0;

        double reactivity = 1.0; // 没开/没采集到音乐律动数据时的固定中等幅度
        if (Droid.MobileSettingsStore.MusicReactiveEnabled && Droid.AudioReactiveCapture.IsActive)
        {
            double raw = Droid.AudioReactiveCapture.RawRatio; // 取值范围 [MinSpeedRatio, MaxSpeedRatio]
            double normalized = (raw - Droid.AudioReactiveCapture.MinSpeedRatio)
                / (Droid.AudioReactiveCapture.MaxSpeedRatio - Droid.AudioReactiveCapture.MinSpeedRatio);
            reactivity = 0.25 + Math.Clamp(normalized, 0, 1) * 1.65; // 映射到 0.25~1.9，拉大反差让"跟着音乐跳"更明显
        }

        double canvasWidth = MusicBarsCanvas.ActualWidth;
        if (canvasWidth <= 0) canvasWidth = 360; // 布局还没跑完那一瞬间的兜底，不是真实屏幕宽度
        double canvasHeight = MusicBarsCanvas.ActualHeight;
        if (canvasHeight <= 0) canvasHeight = 720;
        double centerY = canvasHeight / 2;

        double maxBarHeight = Math.Min(MusicBarMaxHeightCap, canvasHeight * 0.6);

        // 撑满整个可用宽度（两侧各留 MusicBarSideMargin 的安全边距）——间距是算出来的，不是写死的
        // 常量，横屏宽出很多的时候这个间距会自动变大，柱子照样铺到两侧边距为止，不会缩成一小条挤在
        // 正中间
        double usableWidth = Math.Max(0, canvasWidth - MusicBarSideMargin * 2);
        double gap = MusicBarCount > 1
            ? Math.Max(0, (usableWidth - MusicBarCount * MusicBarWidth) / (MusicBarCount - 1))
            : 0;
        double startX = MusicBarSideMargin;

        for (int i = 0; i < _musicBarRects.Length; i++)
        {
            double wave = Math.Abs(Math.Sin(t * MusicBarFrequencies[i] + MusicBarPhases[i]));
            double height = MusicBarMinHeight + maxBarHeight * Math.Clamp(wave * reactivity, 0, 1);
            _musicBarRects[i].Height = height;
            Canvas.SetLeft(_musicBarRects[i], startX + i * (MusicBarWidth + gap));
            Canvas.SetTop(_musicBarRects[i], centerY - height / 2);
        }
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

    /// <summary>手动导入 LRC——联网抓词那几个引擎都不认识的冷门歌/本地翻唱/伴奏版，没有别的办法找到
    /// 词，允许用户自己从手机存储选一个 .lrc 文件顶上去，见 XAML 里 BtnImportLrc 那段注释。跟自动
    /// 抓到的词走的是同一条链路：解析成功就直接当 _currentLines 用，同时存进 LyricsCacheStore（跟
    /// FetchLyricsAsync 存缓存用的同一个 key），下次再放这首歌不用重新导入一遍，直接从缓存命中。
    /// 选文件/解析这几秒歌又换了的话，只把这份词存进缓存，不覆盖新歌当前显示的状态——跟 TranslateAsync
    /// 换歌校验是同一个套路，见该方法注释。</summary>
    private async Task ImportLrcFromFileAsync()
    {
        if (_controller == null) return; // 没有正在播放的歌，不知道这份 LRC 该配给谁

        string trackKey = _currentTitle + "|" + _currentArtist;
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".lrc");
        picker.FileTypeFilter.Add(".txt");

        try
        {
            StorageFile? file = await picker.PickSingleFileAsync();
            if (file == null) return; // 用户自己取消了，不是失败，不用报错

            string content = await FileIO.ReadTextAsync(file);
            var lines = LrcParser.ParseLines(content);
            if (lines.Count == 0)
            {
                Toast.MakeText(global::Android.App.Application.Context,
                    "这份文件没解析出任何一行带时间戳的歌词，确认选对文件了吗", ToastLength.Long)?.Show();
                return;
            }

            _lyricsCache?.Save(trackKey, content);
            if (trackKey == _currentTitle + "|" + _currentArtist)
            {
                _currentLrcContent = content;
                _currentLines = lines;
                _translationLines = null;
                if (_bilingualEnabled) TryEnsureTranslationForCurrentTrack();
                UpdateLyricUi();
            }
            Toast.MakeText(global::Android.App.Application.Context, $"已导入 {lines.Count} 行歌词", ToastLength.Short)?.Show();
        }
        catch (Exception ex)
        {
            Toast.MakeText(global::Android.App.Application.Context, "导入失败：" + ex.Message, ToastLength.Long)?.Show();
        }
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
        TryRegisterActiveSessionsListener(); // 没注册成功过的话顺手再试一次，见该方法顶部注释——权限是这次才补上的话，不用重进这个页面就能自愈
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
                CornerSkinIcon.Source = frames[_iconFrameIndex]; // 右下角那份跟顶部条同步换帧，见 ApplyPalette 里那段注释
            }
        }

        // 多层装饰（layers）各自的逐帧动画，挂在同一个 tick 上一起播，不单开一份——见 ImageFrameState
        _layer0Anim.Tick(IconFrameTickIntervalMs);
        _layer1Anim.Tick(IconFrameTickIntervalMs);

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
        BtnImportLrc.Visibility = Visibility.Collapsed; // 只在下面"确认没找到词"那个分支里重新露出来
        ShowKineticLyricHost(false); // 默认收起来，只有下面真的走到"显示一句正常歌词"这条分支、且开着动感歌词才会翻开

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

        // 确认抓完词、结果就是空列表（区别于上面 _currentLines == null 那个"还没抓完/还没开始抓"的
        // 状态，见 FetchLyricsAsync 里空列表当"确实找不到"那段注释）——不能再往下走逐行匹配那条路：
        // 空列表下面那个 for 循环永远匹配不到任何一行，currentIndex 会一直停在 -1，走到
        // "currentIndex < 0" 那个分支显示空字符串，效果跟"这句歌词之间的正常空白间隙"（比如前奏还
        // 没到第一句歌词）完全一样，用户没法区分"这首歌真的没歌词"还是"歌词还没播到第一句"——这就是
        // 用户反馈的"全屏模式下找不到歌词的歌不会显示任何提示"。这里提前把"确认没有歌词"这句提示
        // 显示出来，播放进度条/时间照样正常显示（只是没有歌词可跟），不提前 return 到最上面那个
        // "还在联网找"分支去。
        if (_currentLines.Count == 0)
        {
            SetLyricPlain("（没找到歌词，可以在下面手动导入 LRC）");
            TxtTranslationLine.Text = "";
            BtnImportLrc.Visibility = Visibility.Visible;
            return;
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

        if (_kineticEnabled)
        {
            ShowKineticLyricHost(true);
            UpdateKineticLyric(lineText, lineStartMs, lineEndMs, positionMs, translationText);
        }
        else
        {
            SetLyricKaraoke(lineText, sungChars);
            TxtTranslationLine.Text = translationText ?? "";
        }
    }

    /// <summary>在"旧的整行居中显示"（LyricStackPanel）和"动感歌词"（KineticLyricHost）两套 UI 之间
    /// 切换——两套互斥，永远只有一套在显示，见 XAML 里 KineticLyricHost 那段注释。
    ///
    /// 状态没变就一个字都不写：UpdateLyricUi 每 200ms 跑一次，每次开头都会先调一遍
    /// ShowKineticLyricHost(false) 兜底、再在真的要显示动感歌词时调 (true)。之前这里不判断、每次都
    /// 硬写一遍 Visibility，等于每个 tick 都把这块 UI「收起来又展开」一次，布局被反复作废，
    /// KineticLyricHost.ActualHeight 一直量不到真实值（收起来的元素排版尺寸是 0），下面
    /// UpdateKineticLyric 里那个「量不到高度就先不冒段」的保护就永远成立——结果就是动感歌词整个不
    /// 出现了。这是上一版真机上「歌词跳动不见了」的直接原因，不是设置没开也不是歌词没抓到。</summary>
    private void ShowKineticLyricHost(bool show)
    {
        var kineticVisibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (KineticLyricHost.Visibility != kineticVisibility) KineticLyricHost.Visibility = kineticVisibility;

        var stackVisibility = show ? Visibility.Collapsed : Visibility.Visible;
        if (LyricStackPanel.Visibility != stackVisibility) LyricStackPanel.Visibility = stackVisibility;
    }

    /// <summary>动感歌词那块区域现在有多高——正常情况直接用 KineticLyricHost.ActualHeight（它就是
    /// Grid 里 Row 1 那一行，天生是"整屏减去顶部条和底部播放控制条"剩下的安全区，见 XAML 注释）。
    /// 刚切过来那一两帧布局还没跑完、量出来是 0 的时候，用"整屏高度 - 顶部条高度 - 底部控制条高度"
    /// 现算一个等价的估算值顶上，而不是像上一版那样直接不显示——量不准顶多位置偏一点，不显示是
    /// 功能整个没了，孰轻孰重很清楚。两条路都拿不到就退到一个保守的固定值。</summary>
    private double GetKineticBandHeight()
    {
        double hostHeight = KineticLyricHost.ActualHeight;
        if (hostHeight > 0) return hostHeight;

        double estimated = RootGrid.ActualHeight - TopBar.ActualHeight - BottomControlsPanel.ActualHeight;

        // 兜底值给一个够摆得下三行字的下限：估算出来接近 0（顶部条/底部条把整屏占满这种理论情况）
        // 的话，下游按行高算出来的字号会小到没法看，还不如退回一个固定的合理值
        return estimated > 200 ? estimated : 400;
    }

    /// <summary>动感歌词——当前行不再整行居中堆着，改成按几个词一组拆成"段"，随着播放进度依次冒出来，
    /// 用户参考的是网上那种"歌词大字、忽大忽小、满屏乱跳"的踩点卡点视频剪辑效果（比如 Instagram/
    /// TikTok 常见的那种歌词卡点视频）。这版是简化实现，没有真的去做那种色差故障风/手写描边/运动
    /// 模糊拖影特效——那些要么得接一整套着色器管线，要么得手绘专门的字体，跟"歌词展示控件"这件事
    /// 已经不是同一个量级的工作量了；这版做的是其中最核心、也是普通 XAML 布局系统就能做到的一层：
    /// "同一句话不再是从头到尾焊死在屏幕正中间同一个位置、同一个字号"，每一段自己的字号/左中右位置/
    /// 上下偏移/倾斜角度都不一样，跟着歌词播放的节奏依次冒出来，已经有那味儿了。
    ///
    /// 冒出来的段不会被顶掉/删除——第一版是"只显示当前这一段、换下一段就把上一段整个换掉"，用户
    /// 反馈这样跳得太快、跟逐字跳没什么区别；改成段与段之间是"叠加"关系：这一行冒出来第一段之后，
    /// 过一会儿第二段在另一个位置冒出来、跟第一段一起留在屏幕上，直到这一行彻底结束、下一行歌词的
    /// 时间戳到了，才把这一行所有段一次性清空、开始重新攒下一行的——见 ClearKineticSegments 在换行
    /// 时机才调用。
    ///
    /// 段与段之间没有真实的时间戳（免费歌词源只给整行时间戳，见 KaraokeTiming 类顶部注释）——用同
    /// 一套"按字符数占比估算"的数学，把这一行的估算演唱时长按每一段的字符数占比切成几份，跟卡拉OK
    /// 逐字上色估算"唱到第几个字"是同一个数据源（KaraokeTiming.EstimateFillDurationMs），只是这边
    /// 用来切"段边界"而不是找"字边界"。纯中文歌词（没有空格分词）会被当成一整句"一段"处理，退化
    /// 成"整句大字居中显示、字号/位置仍然会变"，没法拆成短语级别的跳动——中文歌词本来就没有空格
    /// 可以拿来分词，硬拆的话边界会很奇怪，不如老实展示整句。</summary>
    private void UpdateKineticLyric(string lineText, int lineStartMs, int lineEndMs, int positionMs, string? translationText)
    {
        string lineKey = lineStartMs + "|" + lineText;
        if (lineKey != _kineticWordsForLineKey)
        {
            _kineticWordsForLineKey = lineKey;
            _kineticWords = SplitLineIntoSegmentsWithTiming(lineText, lineStartMs, lineEndMs);
            _kineticRevealedCount = 0;
            ClearKineticSegments(); // 换行了，把上一行攒的那些段一次性清掉，这一行从头重新攒
        }

        TxtKineticTranslation.Text = translationText ?? "";

        if (_kineticWords is not { Length: > 0 })
        {
            // 兜底：这一行没切出任何段（比如整行全是空白，理论上不该出现），照抄整行文本当一段，
            // 总不能什么都不显示
            if (_kineticSegmentViews.Count == 0) AddKineticSegment(lineText, lineKey, 0);
            return;
        }

        // 往外冒段——已经冒出来的不会被顶掉，一直留到换行（见本方法顶部注释那条取舍）。唯一的例外
        // 是"翻页"：一行歌词切出来的段数超过安全区能排下的行数时，第 N 段会绕回第 0 行，正好压在
        // 那一行已经有的那段上面——这时候先把这一页清空、从第 0 行重新开始铺，保证任何时刻屏幕上
        // 最多只有"行数"这么多段、且一行一段，不会重叠，见 AddKineticSegment 顶部注释。
        while (_kineticRevealedCount < _kineticWords.Length && _kineticWords[_kineticRevealedCount].StartMs <= positionMs)
        {
            if (_kineticRevealedCount > 0 && _kineticRevealedCount % KineticSlotAlignments.Length == 0)
            {
                ClearKineticSegments();
            }

            AddKineticSegment(_kineticWords[_kineticRevealedCount].Text, lineKey, _kineticRevealedCount);
            _kineticRevealedCount++;
        }
    }

    /// <summary>把 KineticLyricHost 里当前攒着的那些段（不包括固定不动的 TxtKineticTranslation）
    /// 一次性摘掉——只在真的换到新一行歌词的时候调用，见 UpdateKineticLyric。</summary>
    private void ClearKineticSegments()
    {
        foreach (var view in _kineticSegmentViews)
        {
            KineticLyricHost.Children.Remove(view);
        }
        _kineticSegmentViews.Clear();
    }

    /// <summary>按这一行的估算演唱时长（跟卡拉OK逐字上色同一个数据源），把 lineText 按空格拆出来的
    /// 词每 KineticWordsPerSegment 个分一组当一"段"，再按段的字符数占比分配一段跟自己占比对应的
    /// 时间区间——没有真实的逐段时间戳，这是能拿到的数据里最合理的估算，过渡自然，不是均匀切成几
    /// 等份（一段字多、一段字少的话，分到的时间本来就不该一样长）。</summary>
    private static (string Text, int StartMs, int EndMs)[] SplitLineIntoSegmentsWithTiming(string lineText, int lineStartMs, int lineEndMs)
    {
        var words = lineText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return Array.Empty<(string, int, int)>();

        var segments = new List<string>();
        for (int i = 0; i < words.Length; i += KineticWordsPerSegment)
        {
            segments.Add(string.Join(' ', words.Skip(i).Take(KineticWordsPerSegment)));
        }

        int fillDurationMs = KaraokeTiming.EstimateFillDurationMs(lineText, lineStartMs, lineEndMs);
        int totalChars = lineText.Length;
        var result = new (string, int, int)[segments.Count];
        int charCursor = 0;
        for (int i = 0; i < segments.Count; i++)
        {
            int segStartChar = charCursor;
            int segEndChar = charCursor + segments[i].Length;
            int segStartMs = lineStartMs + (int)((double)segStartChar / totalChars * fillDurationMs);
            int segEndMs = lineStartMs + (int)((double)segEndChar / totalChars * fillDurationMs);
            result[i] = (segments[i], segStartMs, segEndMs);
            charCursor = segEndChar + 1; // +1 跳过这一段后面那一个空格
        }
        return result;
    }

    // 每一段落在哪一"行"——安全区被均分成 KineticSlotAlignments.Length 行，第 N 段落在第
    // N % 行数 行，每行只会有一段。横向对齐方式按行轮流换（左/右/中/左/右），配合每段各自的字号和
    // 倾斜角度，看起来还是"满屏乱跳"，但纵向是规规矩矩一行一段。
    //
    // 为什么非要这么排：用户要求"确保歌词与歌词之间不会重叠"。上一版是给每段各自抽横向位置 + 一个
    // 纵向锚点比例，真机上（尤其横屏，安全区高度只有竖屏的三分之一左右）会出现两段字正面叠在一起
    // 糊成一团的情况——因为"横向对齐不同"根本不保证不重叠：一段居中的长文本照样会横着盖到贴左那段
    // 头上。只有"纵向各占一行、行与行不共享高度"才是真的保证，其它都是概率问题。
    //
    // 行高、字号、倾斜角度三者是绑死的一组数：字号上限 = 行高 × KineticFontRowFillRatio，让一行字
    // （行高约 1.25 倍字号）连同倾斜之后多出来的那点高度，都还留在自己这一行里，不会探到隔壁行。
    // 段数超过行数就翻页（见 UpdateKineticLyric 里的 ClearKineticSegments），不会绕回来压在第 0 行
    // 已经有的那段上面。
    // 只分 3 行，不是更多行——行数直接决定字能多大（字号上限 = 行高 × KineticFontRowFillRatio，
    // 行高 = 安全区高度 / 行数）。之前分 5 行时字号被压到安全区高度的 9% 左右，横屏上只有 30dp 出头，
    // 用户反馈"字体太小了"；改成 3 行之后同样的保证下字号上限翻到安全区高度的 18% 左右，大了一倍。
    // 一行歌词切出来的段数超过 3 段就翻页（见 UpdateKineticLyric），屏幕上任何时候最多 3 段、一行
    // 一段，照样不会重叠。
    private static readonly HorizontalAlignment[] KineticSlotAlignments =
    {
        HorizontalAlignment.Left,
        HorizontalAlignment.Right,
        HorizontalAlignment.Center,
    };

    private const double KineticFontRowFillRatio = 0.55; // 字号占行高的比例，留够行高本身(~1.25倍字号)+倾斜溢出的余量
    private const double KineticMaxRotationDegrees = 5;  // 倾斜角度也压着来，角度越大横向越长的文本纵向溢出越多

    /// <summary>新冒出来一段——现造一个 TextBlock，塞进 KineticLyricHost，从此留在那不会被摘掉
    /// （除非换行，见 ClearKineticSegments）。字号/左中右位置/上下偏移/倾斜角度用
    /// "这一行的 key + 这一段在行内的序号"当随机种子抽一次定下来，同一句歌词倒回去重放（拖进度条/
    /// 上一首又切回来）抽到的还是同一个样子，不是每次都换一副新面孔。
    ///
    /// 位置怎么定：安全区（KineticLyricHost，就是 Grid 的 Row 1，天生是"整屏减去顶部条和底部播放
    /// 控制条"剩下的那块，见 XAML 注释）被均分成固定几行，第 N 段落在第 N%行数 行的正中间，一行
    /// 只放一段，字号封顶在"行高 × KineticFontRowFillRatio"——这样一段字连同倾斜之后多出来的高度
    /// 都还在自己那一行里，段与段之间纵向上物理不可能重叠（用户明确要求"确保歌词与歌词之间不会
    /// 重叠"，横向对齐方式不同并不构成保证，居中的长文本照样会横着盖到贴边那段头上，只有分行才是
    /// 真的保证）。全部按比例算、不写死像素：横屏的安全区高度只有竖屏三分之一左右，写死像素在
    /// 横屏上必然溢出。</summary>
    private void AddKineticSegment(string text, string lineKey, int segmentIndex)
    {
        var rng = new Random((lineKey + "|" + segmentIndex).GetHashCode());

        var textBlock = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Consolas"),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(ToUiColor(_palette.Text)),
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
        };

        double bandHeight = GetKineticBandHeight();
        int rowCount = KineticSlotAlignments.Length;
        double rowHeight = bandHeight / rowCount;
        int rowIndex = segmentIndex % rowCount;

        // 字号：先按文本长短挑一档（长文本给小一点，跟 LyricFontSizeFor 同一个"别顶到溢出"的顾虑），
        // 再被行高按住——行高这一道才是保证不串行的那一道，前面那档只是让长短句看起来有大小变化
        double maxFontSize = text.Length switch
        {
            <= 4 => 88,
            <= 8 => 68,
            <= 14 => 50,
            _ => 36,
        };
        maxFontSize = Math.Min(maxFontSize, rowHeight * KineticFontRowFillRatio);

        // 下限不能反超上限：安全区被压得很扁的时候（分屏、极端小窗口，rowHeight × 填充比 < 20），
        // 上限会掉到 20 以下，如果还硬套一个 20 的下限，下面那行算出来的字号会落在上限和下限之间的
        // 反向区间里，极端情况能算出 0 甚至负数——FontSize 不接受非正数，是会抛异常的，不是画得难看
        // 而已。所以下限先取"上限的 78%"，再跟一个很小的绝对下限取大，最后夹死不超过上限。
        double minFontSize = Math.Min(maxFontSize, Math.Max(8, maxFontSize * 0.78));
        maxFontSize = Math.Max(maxFontSize, minFontSize);
        textBlock.FontSize = minFontSize + rng.NextDouble() * (maxFontSize - minFontSize);

        textBlock.HorizontalAlignment = KineticSlotAlignments[rowIndex];
        // 贴左/贴右的时候留一点内边距，不让字紧贴屏幕边缘被裁到看不全；居中的时候不需要这个边距
        textBlock.Margin = textBlock.HorizontalAlignment == HorizontalAlignment.Center
            ? new Thickness(0)
            : new Thickness(28, 0, 28, 0);

        // 这一行正中间相对整块安全区中心的偏移量——第 0 行在最上面，最后一行在最下面，均分。
        // 不再叠随机抖动：抖动会吃掉行与行之间留的余量，那就等于把"不重叠"这个保证又变回概率问题了，
        // 视觉上的不规则感交给字号/左右位置/倾斜角度这三样去做，纵向老老实实一行一段。
        double offsetY = (rowIndex + 0.5) * rowHeight - bandHeight / 2;

        var transformGroup = new TransformGroup();
        transformGroup.Children.Add(new RotateTransform
        {
            Angle = rng.NextDouble() * KineticMaxRotationDegrees * 2 - KineticMaxRotationDegrees,
        });
        transformGroup.Children.Add(new TranslateTransform { Y = offsetY });
        textBlock.RenderTransform = transformGroup;

        KineticLyricHost.Children.Add(textBlock);
        _kineticSegmentViews.Add(textBlock);
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

    /// <summary>装饰层（LayerIcon0/LayerIcon1）的逐帧播放状态——跟主图标 SkinIcon 那份
    /// _iconFrameBitmaps/_iconFrameElapsedMs/_iconFrameIndex 是同一套"tick 累计时间、攒够一帧的
    /// 时长才切"逻辑，抽成这个小类避免两份重复的帧数组/帧间隔/累计时间/当前帧号字段，跟
    /// FloatingOverlayService.FrameAnimState 是同一个用意（那边画的是 Android 原生 ImageView，这边
    /// 是 Uno 的 Image，重新写一份小的，不是共享同一个类型）。Frames 只有 1 张（或 null）就是静态
    /// 图标/没有图标，Tick 直接跳过；Reset 换一套新数据的同时会立刻把第一帧画出来，不用等下一次
    /// tick 才第一次显示，没有图标就把 Source 清空（配合 ApplyFullScreenLayerAnchor 一起把整个
    /// Image 隐藏掉）。</summary>
    private sealed class ImageFrameState
    {
        private WriteableBitmap[]? _frames;
        private double _durationSeconds;
        private double _elapsedMs;
        private int _index;
        private Microsoft.UI.Xaml.Controls.Image? _target;

        public void Reset(Microsoft.UI.Xaml.Controls.Image? target, WriteableBitmap[]? frames, double durationSeconds)
        {
            _target = target;
            _frames = frames;
            _durationSeconds = durationSeconds;
            _elapsedMs = 0;
            _index = 0;
            if (_target != null) _target.Source = frames is { Length: > 0 } ? frames[0] : null;
        }

        public void Tick(int intervalMs)
        {
            if (_target == null || _frames is not { Length: > 1 }) return;

            _elapsedMs += intervalMs;
            double frameDurationMs = Math.Max(50, _durationSeconds * 1000); // 下限保护，见 FloatingOverlayService.FrameAnimState 同样的注释
            if (_elapsedMs >= frameDurationMs)
            {
                _elapsedMs = 0;
                _index = (_index + 1) % _frames.Length;
                _target.Source = _frames[_index];
            }
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
