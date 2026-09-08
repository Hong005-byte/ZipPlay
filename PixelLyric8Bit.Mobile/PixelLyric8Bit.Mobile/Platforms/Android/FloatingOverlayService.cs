using System.Net.Http;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Media;
using Android.OS;
using Android.Provider;
using Android.Runtime;
using Android.Text;
using Android.Text.Style;
using Android.Views;
using Android.Widget;
using System.Linq;
using AndroidMediaSession = Android.Media.Session;

namespace PixelLyric8Bit.Mobile.Droid;

/// <summary>
/// 真正的"悬浮窗"——不是 Uno 渲染的一个页面/窗口，是一个前台 Service 自己用 IWindowManager 往系统
/// 窗口层加一个原生 Android View（还是一个朴素的 TextView，没有接 Uno/Skia 渲染管线；Uno 的渲染整套
/// 是绑在 MainActivity 那个 Activity 窗口上的，"脱离 Activity、飘在所有 App 上面"这件事本身跟 Uno 的
/// 单项目模型没关系，是 Android 系统层面"这块窗口该由谁来管"的问题）。视觉上现在接了一版精简皮肤
/// （见 ApplySkin）：背景/边框/文字配色跟着 MobileSkinCatalog 里选的那套走，边框还带一圈呼吸感的
/// 淡入淡出，图标（内置皮肤或者客制化主题的 icon.rows/icon.frames）也画得出来了，>1 帧的话还会按
/// frameDuration 循环播放（见 FrameAnimState.Tick）。客制化主题专属的多层装饰（layers，最多 2 个，
/// 见 ApplySkin 里 layer0/layer1 那两段）和点击切姿势（icon.actions，见 CycleIconAction）也接上了；
/// animation 字段的 6 种"简单招式"（pulse/twinkle/sway/spin/flicker/bob，见 IconMotionState）也接了
/// 固定节奏的版本——drift/fall/walk 这三招桌面版要专属渲染轨道（三重影飘过/飘落、装饰带里来回走），
/// 这版还没做，选了这三招图标就静止不动，不报错也不崩。皮肤专属律动动画里"跟着音乐响度/鼓点实时
/// 变速"这一层（musicReactive 开关）还没有——那套依赖 Android 端的实时音频采集，目前还没有对应
/// 桌面版 WASAPI 回环采集的那一整套管线，musicReactive 暂时不生效，帧动画/装饰层动画/这几招简单
/// 招式都是固定节奏播放，见 IconFrameTick/MotionTick。
///
/// 悬浮窗权限（SYSTEM_ALERT_WINDOW）是特殊权限，跟"通知使用权"（MediaNotificationListenerService）
/// 同一个套路：普通的运行时权限弹窗申请不了，必须让用户自己去系统设置里手动开一次。
///
/// 数据这条链路：订阅 MediaSessionManager 的"活跃会话列表变化"+ 当前 MediaController 的
/// MediaController.Callback（播放状态/元数据变化），歌曲变了先查 LyricsCacheStore 本地缓存，没有才
/// 用 PixelLyric8Bit.Core 的 LyricsFetcher（跟桌面版同一份多引擎并发抓词逻辑）联网抓一份 LRC 存起来；
/// 显示这一行歌词靠本地 PlaybackPositionEstimator（同一份数学桌面版也在用）按锚点插值算出"此刻播放
/// 到哪了"，不是每次画面刷新都重新问一次系统——这就是桌面版"歌词按时间戳同步显示 + 本地插值省开销"
/// 这件事，在 Android 上用真实数据 + 事件驱动跑通（上一版是定时轮询 MediaSessionManager，效率和
/// 实时性都更差，见下面 RenderTick 的注释）。
///
/// 歌词功能这块也补齐了几样：卡拉OK 逐字上色（Core 的 KaraokeTiming 估算唱到第几个字，SpannableString
/// 上两级颜色）、同步偏移（设置页 ±50ms 微调，加在"挑哪一行歌词"这一步）、双语歌词（LyricsTranslator
/// 现场调 Google 翻译网页版接口，翻完存缓存），三个都是 MobileSettingsStore 里的设置，悬浮窗开着的
/// 时候改也会跟着生效，见 RefreshSettingsFromStore。
///
/// 听歌统计 + 成就：跟桌面版 MainWindow.ListeningStats.cs 同一个思路——按真实墙钟时间攒
/// _pendingListenSeconds，攒够阈值才 flush 进 ListeningStatsFileStore 存盘（不是每个 tick 都写盘），
/// 换歌/换会话/悬浮窗关闭这几个时机也会补flush 一次零头，不丢最后几秒；每次真的写了新数据就顺手评估
/// 一遍 8 个 AchievementCalculator 成就，MobileAchievementUnlockTracker 记"见过哪些"，刚解锁的用系统
/// Toast 弹一下，见 FlushListeningStats。
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeSpecialUse | ForegroundService.TypeMediaProjection)]
public class FloatingOverlayService : Service
{
    private const string ChannelId = "zipplay_overlay";
    private const int NotificationId = 1001;

    // 本地渲染 tick——只做"锚点插值 + 找当前该显示哪一行 + 文字没变就不碰 TextView"这几步纯本地计算，
    // 不再每次都去问系统"现在到哪了"（那是一次跨进程 Binder 调用）。真正的数据更新（歌曲变了/播放位置
    // 变了/暂停了）全部靠下面的 MediaController.Callback 推过来，这个 tick 只负责"把已知锚点画出来"，
    // 所以间隔可以比之前的 1000ms 轮询更短（歌词切换看起来更跟手），开销反而更低。
    private const int RenderIntervalMs = 200;

    private IWindowManager? _windowManager;
    private FrameLayout? _rootFrame; // 真正加进 WindowManager 的是这一个——里面装着居中的 _overlayContainer（皮肤主体）
                                      // 加最多 2 个贴在角上的装饰层（_layer0View/_layer1View），拖动/点击手势也挂在这上面
    private LinearLayout? _overlayContainer; // 图标 + 文字横排的那个"药丸"卡片，本身不再直接加进 WindowManager，见 _rootFrame
    private ImageView? _iconView;
    private TextView? _overlayView;
    private ImageView? _layer0View; // 多层装饰第 1 层，见 ApplySkin 里 layer0 那段——没有客制化主题/主题没配 layers 就一直 Gone
    private ImageView? _layer1View; // 多层装饰第 2 层，规则同上
    private WindowManagerLayoutParams? _layoutParams;
    private Handler? _mainHandler;
    private Action? _renderAction;
    private string? _lastRenderedText;

    private readonly HttpClient _httpClient = new();
    private PixelLyric8BitFix.LyricsFetcher? _lyricsFetcher;
    private PixelLyric8BitFix.LyricsCacheStore? _lyricsCache;
    private PixelLyric8BitFix.LyricsTranslator? _translator;

    // ── 精简皮肤：配色 + 边框呼吸动效，见 ApplySkin ─────────────────────────────────
    private const int PulseIntervalMs = 120; // 呼吸动效自己的 tick，比歌词渲染 tick（200ms）更密一点，看着才顺滑
    private const int StrokeWidthDp = 2;
    private const int CornerRadiusDp = 14;
    private const int IconSizeDp = 28; // 悬浮窗本来就是一小条，图标不能喧宾夺主，比桌面版装饰栏图标（20~52px）偏小一档
    private GradientDrawable? _overlayBackground;
    private PixelLyric8BitFix.MobileSkinPalette _currentPalette = PixelLyric8BitFix.MobileSkinCatalog.Find(null);
    private PixelLyric8BitFix.MobileCustomThemeStore? _customThemeStore;
    private SettingsPrefListener? _settingsPrefListener;
    private Action? _pulseAction;
    private DateTimeOffset _pulseStartTime;
    private int _strokeWidthPx;

    // ── 客制化主题的逐帧图标动画（icon.frames），见 ApplySkin/IconFrameTick ────────────────
    // 内置皮肤（MobileSkinIconCatalog）目前都是单帧，只有客制化主题的 icon.frames 会走到这一段；
    // 桌面版对应逻辑在 MainWindow.Skins.cs 的 UpdateCustomIconFrameAnimation，那边还会按音乐响度
    // 变速（musicReactive），这边没有——Android 端还没有 AudioVisualizer 那一整套音频采集/分析
    // 管线，帧间隔就按 JSON 里的 frameDuration 老老实实播放，不做变速这一层
    private const int IconFrameTickIntervalMs = 100; // 单开一份 tick，比呼吸动效(120ms)/歌词渲染(200ms)独立，互不干扰
    private const int LayerIconSizeDp = 18; // 装饰层比主图标（28dp）小一档，是"点缀"不是喧宾夺主
    private const int LayerMarginDp = 4; // 装饰层贴角的内缩距离，避免正好卡在圆角卡片被裁掉的那一小块
    private int _layerMarginPx;

    // 主图标（含点击切姿势换上来的那一帧）+ 两个装饰层，三处都是同一套"tick 累计时间、攒够一帧的
    // 时长才切"逻辑，抽成 FrameAnimState 复用，见该类定义
    private readonly FrameAnimState _mainIconAnim = new();
    private readonly FrameAnimState _layer0Anim = new();
    private readonly FrameAnimState _layer1Anim = new();
    private Action? _iconFrameAction;

    // ── 🖱️ 点击切姿势（icon.actions），见 CycleIconAction/EvaluateAutoSwitchIconAction ───────
    // "动作 0" 是主图标自己的 Rows/Frames，不用存进这个列表；_baseIconFrames/_baseIconFrameDurationSeconds
    // 是切回动作 0 时要用的那一份，跟 _mainIconAnim 分开存是因为 _mainIconAnim 当前显示的可能是某个
    // 动作、不是主图标本身，得留一份"原样"才能切得回去
    private Bitmap[]? _baseIconFrames;
    private double _baseIconFrameDurationSeconds;
    private List<PixelLyric8BitFix.CustomThemeIconAction>? _iconActions;
    private Bitmap[][]? _actionFrameBitmaps; // 跟 _iconActions 一一对应，ApplySkin 时就预渲染好，点击只是切现成位图
    private double[]? _actionFrameDurations;
    private int _currentIconActionIndex; // 0 = 主图标本身，1.. 对应 _iconActions[index-1]

    // 数据驱动的自动切换阈值（CustomThemeIconAction.AutoSwitchAfterSeconds）要用的"这首歌连续播放了
    // 多久"——暂停不计时、换歌清零，见 UpdateListeningStats 里顺手累加的那一段和 HandleMetadataChanged
    // 换歌时的清零，跟桌面版 MainWindow.ListeningStats.cs 的 _customIconContinuousTrackSeconds 是
    // 同一个口径
    private double _continuousPlaySeconds;

    // ── animation 字段的 6 种简单招式（pulse/twinkle/sway/spin/flicker/bob），见 IconMotionState ──
    // 主图标 + 两个装饰层各自一份独立状态（互不干扰，比如主图标 sway、装饰层 twinkle 同时播不会
    // 打架）；_themeIconAnimationType/_themeIconAnimationDuration 是主图标顶层那份 animation，切姿势
    // （CycleIconAction）时如果新姿势没有自己的 animation 就落回这一份，见 ApplyCurrentIconActionFrame
    private const int MotionTickIntervalMs = 50; // 20fps，这几招都是缓慢的呼吸/摆动，不需要更密的 tick
    private readonly IconMotionState _mainIconMotion = new();
    private readonly IconMotionState _layer0Motion = new();
    private readonly IconMotionState _layer1Motion = new();
    private Action? _motionTickAction;
    private string? _themeIconAnimationType;
    private double? _themeIconAnimationDuration;
    private bool _themeIconMusicReactive;
    private string? _themeIconSensitivity;

    // ── 歌词同步偏移 / 卡拉OK / 双语——三个都是设置页可以随时改的开关，见 RefreshSettingsFromStore ──
    private int _syncOffsetMs;
    private bool _karaokeEnabled = true;
    private bool _bilingualEnabled;

    // ── 听歌统计 + 成就，见 FlushListeningStats ───────────────────────────────────
    private const double StatsFlushThresholdSeconds = 10; // 攒够这么多秒才 flush 进存档，不是每个 200ms tick 都写盘
    private const double StatsMaxTickGapSeconds = 3; // 两次 tick 之间隔太久（比如系统把 Service 挂起了一阵）就不计入，避免算出离谱的时长
    private PixelLyric8BitFix.ListeningStatsFileStore? _statsStore;
    private PixelLyric8BitFix.MobileAchievementUnlockTracker? _unlockTracker;
    private PixelLyric8BitFix.ListeningStats _stats = new();
    private double _pendingListenSeconds;
    private DateTimeOffset _lastStatsTickTime;

    // ── 媒体会话订阅：活跃会话列表变了（新 App 开始播/原来那个停了）就重新绑定 ──────────────
    private AndroidMediaSession.MediaSessionManager? _sessionManager;
    private ActiveSessionsListener? _activeSessionsListener;
    private AndroidMediaSession.MediaController? _controller;
    private PlaybackCallback? _controllerCallback;

    // ── 当前绑定的这个会话报出来的信息 ──────────────────────────────────────────────
    private string _currentTitle = "";
    private string _currentArtist = "";
    private TimeSpan _totalDuration = TimeSpan.Zero;

    // ── 播放位置锚点：给 PlaybackPositionEstimator 用的那三样 ─────────────────────────
    private TimeSpan _anchorPosition = TimeSpan.Zero;
    private DateTimeOffset _anchorTime = DateTimeOffset.Now;
    private bool _isPlaying;
    private double _playbackRate = 1.0;

    // ── 歌词抓取 ───────────────────────────────────────────────────────────────
    private string? _fetchedForTrackKey; // "标题|艺人"，用来判断歌曲是不是变了，变了才重新抓词，不然每次事件都联网一次
    private List<(int TimeMs, string Text)>? _currentLines;
    private string? _currentLrcContent; // _currentLines 对应的原始 LRC 文本——双语翻译要用这份原文，不是解析完的行
    private List<(int TimeMs, string Text)>? _translationLines; // 双语歌词的翻译行；没开双语/还没翻完/翻译失败都是 null
    private bool _fetchInFlight;
    private System.Threading.CancellationTokenSource? _fetchCts;

    // 🖼️ 歌词分享卡片——UpdateOverlayText 每 tick 顺手把"这一刻真的显示出来的那一行原文"记一份在这，
    // 跟 _lastRenderedText 不是一回事：那个是"最后一次真的推给 TextView 的完整字符串"（可能是提示语，
    // 也可能是 SpannableString），这个是"干净的一行歌词原文，没有就是 null"，点一下悬浮窗触发分享
    // （见 ShowOverlay 里的 Touch 手势）时只需要这个，不用反过来从渲染结果里剥
    private string? _currentLyricLine;

    public static bool IsRunning { get; private set; }

    /// <summary>正在跑的这个 Service 实例——AudioReactiveCapture.OnConsentGranted 拿到用户同意后要
    /// 回调 EnsureMediaProjectionForegroundType，但那边是个静态类、没有这个 Service 的引用，见该方法
    /// 顶部注释为什么必须是这个时序。悬浮窗没开着的话是 null（音乐律动那个勾选框本来就要求先开悬浮窗
    /// 才能勾，见 PermissionsPage.ChkMusicReactive_Toggled，正常情况不会在 null 的时候被调用）。</summary>
    public static FloatingOverlayService? Instance { get; private set; }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        Instance = this;
        StartForegroundWithNotification(includeMediaProjectionType: false);
        try
        {
            ShowOverlay();
        }
        catch (Exception ex)
        {
            // 真机踩过的坑：ShowOverlay 里头有一步（比如媒体会话订阅，见 TryRegisterActiveSessionsListener
            // 顶部注释）在某些设备/权限组合下会抛异常，一旦冒出这里没人接，整个 App 进程会被系统直接
            // 带崩——用户体感就是"点了显示悬浮窗、App 卡死出不来"，得靠系统"应用无响应"强制关闭才能
            // 脱身。接住、把这次已经加了一半的悬浮窗清理掉、停掉这个前台服务，比让整个进程崩溃体验好
            // 得多，至少 App 主界面还能正常用、能看到发生了什么。
            global::Android.Util.Log.Error("ZipPlayOverlay", "ShowOverlay failed: " + ex);
            try { RemoveOverlay(); } catch { /* 清理阶段本身不该再抛出新的异常 */ }
            Toast.MakeText(this, "悬浮窗启动失败，请检查悬浮窗/通知使用权权限后重试", ToastLength.Long)?.Show();
            Instance = null;
            StopSelf();
            return StartCommandResult.NotSticky;
        }
        IsRunning = true;
        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        RemoveOverlay();
        _fetchCts?.Cancel();
        IsRunning = false;
        Instance = null;
        base.OnDestroy();
    }

    // Android 8 (API 26) 起，前台服务必须配一条常驻通知——这是系统强制的，不是我们自己想加的，
    // 用户会在通知栏看到"ZipPlay 悬浮歌词正在运行"这样一条提示，这也是让用户知道"这个悬浮窗
    // 是怎么冒出来的、想关掉去哪关"的正常渠道。
    //
    // includeMediaProjectionType 这个参数是真机踩出来的第二个坑（第一个坑见下面注释）：早期版本
    // 一直无条件带上 ForegroundService.TypeMediaProjection（不管音乐律动有没有开），这在这台测试机
    // （targetSdk 36，更新的系统版本）上会导致悬浮窗一开就崩——logcat 抓到的真实异常是
    // `SecurityException: Starting FGS with type mediaProjection ... requires permissions: ...
    // any of the permissions allOf=false [CAPTURE_VIDEO_OUTPUT, android:project_media]`，也就是说
    // 新版本还多了一条反过来的要求：declare 这个类型本身就要求这一刻已经有 project_media 这个
    // app-op（用户在系统同意框上点了同意才会有），不是"先声明类型、再去拿 token"这一个方向的要求
    // 就够了。所以现在默认不带这个类型（普通悬浮窗场景走不到 MediaProjection 这条线，声明了反而
    // 会被新系统直接拒绝整个前台服务），只有 EnsureMediaProjectionForegroundType 在"用户刚同意
    // 完、真的要去拿 token 之前"这个窄窗口里才会带上。
    private void StartForegroundWithNotification(bool includeMediaProjectionType)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(ChannelId, "ZipPlay 悬浮歌词", NotificationImportance.Low);
            var manager = (NotificationManager)GetSystemService(NotificationService)!;
            manager.CreateNotificationChannel(channel);
        }

        var notification = new Notification.Builder(this, ChannelId)
            .SetContentTitle("ZipPlay 悬浮歌词")
            .SetContentText("悬浮窗正在显示")
            .SetSmallIcon(global::Android.Resource.Drawable.IcMenuView)
            .SetOngoing(true)
            .Build();

        // 3 参数重载（带 foregroundServiceType）是 API 29 起才有的，配合上面 [Service] 特性上声明的
        // ForegroundServiceType 一起，两边都要对得上，缺一个都会在 API 34+ 的设备上直接崩。
        // TypeMediaProjection 只在 includeMediaProjectionType 为 true 时才带上——见本方法顶部注释这条
        // 新踩到的坑；老坑（"拿 token 这一刻之前就已经是这个类型的前台服务"，见
        // EnsureMediaProjectionForegroundType 顶部注释）仍然存在，两条要求叠在一起，唯一站得住的
        // 时机窗口就是 EnsureMediaProjectionForegroundType 被调用的那一刻。
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
        {
            var type = ForegroundService.TypeSpecialUse;
            if (includeMediaProjectionType) type |= ForegroundService.TypeMediaProjection;
            StartForeground(NotificationId, notification, type);
        }
        else
        {
            StartForeground(NotificationId, notification);
        }
    }

    /// <summary>AudioReactiveCapture.OnConsentGranted 在真的调 MediaProjectionManager.GetMediaProjection
    /// 之前必须先调这个——这是两条互相矛盾的系统要求叠加后唯一站得住的时机：
    /// 1）（老坑）拿 token 这一刻之前，前台服务就必须已经是 TypeMediaProjection，见
    ///    StartForegroundWithNotification 里 "Media projections require a foreground service of type"
    ///    那段历史注释；
    /// 2）（新坑，这次真机在 targetSdk 36 的设备上踩到的）declare 这个类型本身要求这一刻已经有
    ///    project_media 这个 app-op，而这个 app-op 是用户在系统同意框上点了同意才会有——不能在
    ///    用户同意之前就声明。
    /// 两条要求的交集只剩"用户点完同意框、GetMediaProjection 还没调"这一刻，所以这个方法只能由
    /// OnConsentGranted 在那个精确的时间点调用，不能提前到悬浮窗刚开的时候（见
    /// StartForegroundWithNotification 默认 includeMediaProjectionType=false），也不能推迟到
    /// AudioRecord 真的开始读数据之后。</summary>
    public void EnsureMediaProjectionForegroundType() => StartForegroundWithNotification(includeMediaProjectionType: true);

    /// <summary>AudioReactiveCapture.IsActive 变了（拿到/收回了 MediaProjection token）就重新调一遍
    /// StartForegroundWithNotification，把前台服务类型更新成当前实际在用的那一份——主要是收回 token
    /// 停止采集之后把 TypeMediaProjection 这个类型摘掉（EnsureMediaProjectionForegroundType 在拿
    /// token 之前已经把它加上了，这里补的是反过来这一半）。事件可能从别的线程触发，这里统一 Post
    /// 回主 Handler 再碰 Service API，不直接在触发线程上跑。</summary>
    private void RefreshForegroundServiceType() =>
        _mainHandler?.Post(() => StartForegroundWithNotification(AudioReactiveCapture.IsActive));

    private void ShowOverlay()
    {
        if (_overlayView != null) return; // 已经显示了，不重复加一份

        // IWindowManager 是 Java 接口（android.view.WindowManager），不是具体类——.NET for Android
        // 绑定接口的时候，GetSystemService 返回的 Java.Lang.Object 得用 JavaCast<T>() 转，普通 C# 强制
        // 转型（(IWindowManager)xxx）对绑定的 Java 接口不认，会直接抛 InvalidCastException（真机上崩过
        // 一次才发现）。NotificationManager 是具体类不是接口，所以上面 StartForegroundWithNotification
        // 那边的普通强转没事，只有接口类型才有这个坑。
        _windowManager = GetSystemService(WindowService)!.JavaCast<IWindowManager>();

        // 图标 + 文字横向并排——阶段 6 之前这里只有一个 TextView，现在皮肤图标（内置 21 套 + 客制化
        // 主题自己的 icon.rows）画出来搁前面，没有图标数据的皮肤/主题就把 ImageView 隐藏掉，退回
        // 纯文字，不会留一块空白占位
        _iconView = new ImageView(this) { Visibility = ViewStates.Gone };
        float density = Resources?.DisplayMetrics?.Density ?? 3f;
        int iconSizePx = (int)(IconSizeDp * density);
        int layerSizePx = (int)(LayerIconSizeDp * density);
        _layerMarginPx = (int)(LayerMarginDp * density);

        _overlayView = new TextView(this)
        {
            Text = "ZipPlay 悬浮歌词骨架\n（长按拖动试试，正在连接系统媒体会话…）",
            TextSize = 14,
        };

        _overlayContainer = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Horizontal };
        _overlayContainer.SetGravity(GravityFlags.CenterVertical);
        _overlayContainer.AddView(_iconView, new LinearLayout.LayoutParams(iconSizePx, iconSizePx) { RightMargin = (int)(10 * density) });
        _overlayContainer.AddView(_overlayView, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));
        _overlayContainer.SetPadding(32, 20, 32, 20);

        // 多层装饰（layers）——最多 2 个，各自贴在卡片四个角之一，见 ApplySkin。真正加进 WindowManager
        // 的从 _overlayContainer 换成这一层 FrameLayout：_overlayContainer 本身居中摆放，两个装饰层
        // 用 Gravity 贴角，具体贴哪个角、显不显示是 ApplySkin 每次套主题时现算的（ApplyLayerAnchor），
        // 这里先建好两个默认 Gone 的 ImageView 占位
        _layer0View = new ImageView(this) { Visibility = ViewStates.Gone };
        _layer1View = new ImageView(this) { Visibility = ViewStates.Gone };

        _rootFrame = new FrameLayout(this);
        _rootFrame.AddView(_overlayContainer, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { Gravity = GravityFlags.Center });
        _rootFrame.AddView(_layer0View, new FrameLayout.LayoutParams(layerSizePx, layerSizePx) { Gravity = GravityFlags.Top | GravityFlags.Left, LeftMargin = _layerMarginPx, TopMargin = _layerMarginPx });
        _rootFrame.AddView(_layer1View, new FrameLayout.LayoutParams(layerSizePx, layerSizePx) { Gravity = GravityFlags.Top | GravityFlags.Left, LeftMargin = _layerMarginPx, TopMargin = _layerMarginPx });

        // 客制化主题跟统计一样存 FilesDir（持久私有存储），不是 CacheDir——用户手写/粘贴进去的东西，
        // 不该被系统当"可以随便清掉的缓存"处理掉
        _customThemeStore = new PixelLyric8BitFix.MobileCustomThemeStore(System.IO.Path.Combine(FilesDir!.AbsolutePath, "custom_themes"));

        ApplySkin(MobileSettingsStore.SelectedSkinId);
        _syncOffsetMs = MobileSettingsStore.SyncOffsetMs;
        _karaokeEnabled = MobileSettingsStore.KaraokeEnabled;
        _bilingualEnabled = MobileSettingsStore.BilingualEnabled;

        var overlayType = Build.VERSION.SdkInt >= BuildVersionCodes.O
            ? WindowManagerTypes.ApplicationOverlay
            : WindowManagerTypes.Phone;

        _layoutParams = new WindowManagerLayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.WrapContent,
            overlayType,
            WindowManagerFlags.NotFocusable | WindowManagerFlags.LayoutInScreen,
            Format.Translucent)
        {
            Gravity = GravityFlags.Top | GravityFlags.CenterHorizontal,
            Y = 150,
        };

        // 拖动手势——按住拖到屏幕任意位置，这是悬浮窗最基本的交互，不给拖的话跟一张固定贴纸没区别。
        // 手动记初始触点+初始窗口位置，松手不用做任何事，Android 的 WindowManager 会记着这个
        // LayoutParams 对象最后一次 UpdateViewLayout 传的位置，不用自己再存一份。挂在 _rootFrame 上
        // （不是 _overlayContainer）——现在整个悬浮窗是"药丸卡片 + 两个角上的装饰层"这一整块，拖动
        // 手势要覆盖这一整块，不能只有卡片本身能拖、摸到装饰层那块地方就拖不动。
        //
        // 顺手在这上面加了两种"点一下（不是拖）"的反应：点在图标上、且当前主题真的配了 icon.actions
        // 就循环切一次姿势（CycleIconAction，呼应桌面版 MainWindow.SkinInteractions.cs 的
        // CustomIcon_MouseLeftButtonDown）；点在悬浮窗其它地方（文字/装饰层/空白）还是老行为，分享
        // 当前这句歌词。按下到松手之间移动距离没超过 tapMoveThresholdPx 才算"点一下"，不是拖动，
        // 见 MotionEventActions.Up；跟拖动共用同一个手势识别器而不是单独挂 Click，是因为悬浮窗一直在
        // 拦截触摸事件做拖动，另外挂的 Click 永远收不到事件
        float touchStartX = 0, touchStartY = 0;
        int windowStartX = 0, windowStartY = 0;
        bool movedPastTapThreshold = false;
        float tapMoveThresholdPx = 12 * density;
        _rootFrame.Touch += (s, e) =>
        {
            var ev = e.Event!;
            switch (ev.Action)
            {
                case MotionEventActions.Down:
                    windowStartX = _layoutParams.X;
                    windowStartY = _layoutParams.Y;
                    touchStartX = ev.RawX;
                    touchStartY = ev.RawY;
                    movedPastTapThreshold = false;
                    e.Handled = true;
                    break;
                case MotionEventActions.Move:
                    float dx = ev.RawX - touchStartX, dy = ev.RawY - touchStartY;
                    if (Math.Abs(dx) > tapMoveThresholdPx || Math.Abs(dy) > tapMoveThresholdPx) movedPastTapThreshold = true;
                    _layoutParams.X = windowStartX + (int)dx;
                    _layoutParams.Y = windowStartY + (int)dy;
                    _windowManager.UpdateViewLayout(_rootFrame, _layoutParams);
                    e.Handled = true;
                    break;
                case MotionEventActions.Up:
                    if (!movedPastTapThreshold)
                    {
                        if (_iconActions is { Count: > 0 } && IsPointInsideIconView(ev.RawX, ev.RawY)) CycleIconAction();
                        else ShareCurrentLyricLine();
                    }
                    e.Handled = true;
                    break;
            }
        };

        _windowManager.AddView(_rootFrame, _layoutParams);

        _lyricsFetcher = new PixelLyric8BitFix.LyricsFetcher(_httpClient);
        _lyricsCache = new PixelLyric8BitFix.LyricsCacheStore(System.IO.Path.Combine(CacheDir!.AbsolutePath, "lyrics_cache"));
        _translator = new PixelLyric8BitFix.LyricsTranslator(_httpClient);

        // 统计/成就存 FilesDir 不是 CacheDir——这两个目录语义不一样，CacheDir 系统缺存储空间的时候
        // 可能被自动清掉（歌词缓存清了就清了，重新抓一次而已），FilesDir 是持久私有存储，不会被系统
        // 主动清理，听歌记录这种数据丢了用户会有感知，得放在更靠得住的地方
        _statsStore = new PixelLyric8BitFix.ListeningStatsFileStore(System.IO.Path.Combine(FilesDir!.AbsolutePath, "stats.json"));
        _stats = _statsStore.Load();
        _unlockTracker = new PixelLyric8BitFix.MobileAchievementUnlockTracker(System.IO.Path.Combine(FilesDir!.AbsolutePath, "achievement_unlock_seen.json"));
        _lastStatsTickTime = DateTimeOffset.Now;

        _mainHandler = new Handler(Looper.MainLooper!);

        BindMediaSessionManager();

        _renderAction = RenderTick;
        _mainHandler.Post(_renderAction); // 立刻先画一次，不用等第一个 RenderIntervalMs 过去才有内容

        _pulseStartTime = DateTimeOffset.Now;
        _pulseAction = PulseTick;
        _mainHandler.Post(_pulseAction);

        _iconFrameAction = IconFrameTick;
        _mainHandler.Post(_iconFrameAction);

        _motionTickAction = MotionTick;
        _mainHandler.Post(_motionTickAction);

        // 皮肤/同步偏移/卡拉OK/双语都是在 MainPage 的设置页改的，悬浮窗这边是另一个进程/组件——不会
        // 自动知道设置变了，靠 SharedPreferences 自带的变更通知订阅一下，改了哪一项，悬浮窗（哪怕已经
        // 开着）都立刻跟着生效，不用先隐藏再重新显示一次才生效
        _settingsPrefListener = new SettingsPrefListener(() => _mainHandler?.Post(RefreshSettingsFromStore));
        MobileSettingsStore.RegisterChangeListener(_settingsPrefListener);

        // 皮肤音乐律动——AudioReactiveCapture 是静态类（MainActivity 拿到用户同意后从那边直接调），
        // 这边订阅它的状态变化，好在真的开始/停止采集的时候把前台服务类型跟着更新，见
        // RefreshForegroundServiceType
        AudioReactiveCapture.ActiveChanged += RefreshForegroundServiceType;
    }

    // ── 精简皮肤：配色 + 边框呼吸动效 ─────────────────────────────────────────────

    /// <summary>把选中的皮肤配色 + 图标（主图标 + 最多 2 个装饰层）套到悬浮窗上——背景/边框颜色来自
    /// MobileSkinPalette，边框宽度/圆角只在第一次算一遍（用的是 dp -> px 换算，屏幕密度中途不会变），
    /// 换皮肤只是换颜色，不用重新创建 Drawable 对象。主图标来自 MobileSkinIconCatalog（内置皮肤，
    /// 目前都是单帧）或者客制化主题自己的 icon.rows/icon.frames；点击切姿势（icon.actions）和多层
    /// 装饰（layers）只有客制化主题才有，内置皮肤没有这两样，见 RenderIconFrames。</summary>
    private void ApplySkin(string skinId)
    {
        Bitmap[]? mainFrames = null; // 主图标预渲染好的帧数组（长度 1 就是静态图），null 就是这套皮肤压根没有图标
        double mainFrameDurationSeconds = 0;
        List<PixelLyric8BitFix.CustomThemeIconAction>? actions = null;
        Bitmap[][]? actionFrames = null;
        double[]? actionDurations = null;
        Bitmap[]? layer0Frames = null; double layer0Duration = 0; string? layer0Anchor = null;
        Bitmap[]? layer1Frames = null; double layer1Duration = 0; string? layer1Anchor = null;
        string? mainAnimationType = null; double? mainAnimationDuration = null;
        bool mainMusicReactive = false; string? mainSensitivity = null;
        string? layer0AnimationType = null; double? layer0AnimationDuration = null;
        bool layer0MusicReactive = false; string? layer0Sensitivity = null;
        string? layer1AnimationType = null; double? layer1AnimationDuration = null;
        bool layer1MusicReactive = false; string? layer1Sensitivity = null;

        // "custom:文件名" 是选了个客制化主题——去 MobileCustomThemeStore 查，查不到（文件被删了/坏了）
        // 就落回内置表默认那一套，不能让悬浮窗因为一个坏掉的自定义主题直接失去配色
        if (skinId.StartsWith(PixelLyric8BitFix.MobileSkinCatalog.CustomThemePrefix, StringComparison.Ordinal))
        {
            string fileName = skinId[PixelLyric8BitFix.MobileSkinCatalog.CustomThemePrefix.Length..];
            var customTheme = _customThemeStore?.Load(fileName);
            if (customTheme != null)
            {
                _currentPalette = PixelLyric8BitFix.MobileSkinCatalog.FromCustomTheme(customTheme);
                var themeIcon = customTheme.Icon;

                mainAnimationType = customTheme.Animation?.Type;
                mainAnimationDuration = customTheme.Animation?.Duration;
                mainMusicReactive = customTheme.Animation?.MusicReactive ?? false;
                mainSensitivity = customTheme.Animation?.Sensitivity;

                if (themeIcon != null)
                {
                    (mainFrames, mainFrameDurationSeconds) = RenderIconFrames(themeIcon);

                    // 点击切姿势——每个动作各自预渲染一份帧数组存起来，点击的时候（CycleIconAction）
                    // 直接切现成的位图，不用现画。一个动作没给 Frames（理论上校验会拦，这里兜底）就
                    // 落回主图标自己那份，总不能切出一个空白图标
                    if (themeIcon.Actions is { Count: > 0 } themeActions)
                    {
                        actions = themeActions;
                        actionFrames = new Bitmap[themeActions.Count][];
                        actionDurations = new double[themeActions.Count];
                        var actionPalette = PixelLyric8BitFix.CustomThemeValidator.BuildIconPalette(themeIcon);
                        for (int i = 0; i < themeActions.Count; i++)
                        {
                            var action = themeActions[i];
                            var frameRows = (action.Frames ?? new List<List<string>>()).Select(f => f.ToArray()).ToList();
                            actionFrames[i] = frameRows.Count > 0
                                ? MobilePixelIconRenderer.RenderFrames(frameRows, actionPalette)
                                : (mainFrames ?? Array.Empty<Bitmap>());
                            actionDurations[i] = action.FrameDuration is double d && d > 0
                                ? d
                                : PixelLyric8BitFix.CustomThemeValidator.GetFrameDurationSeconds(themeIcon);
                        }
                    }
                }

                // 多层装饰——最多 2 个，各自贴在卡片四个角之一，见 ApplyLayerAnchor；layer.animation
                // 走跟主图标同一套 IconMotionState（6 种简单招式，drift/fall/walk 静止不动），见类顶部注释
                if (customTheme.Layers is { Count: > 0 } layers)
                {
                    if (layers.Count > 0 && layers[0].Icon != null)
                    {
                        (layer0Frames, layer0Duration) = RenderIconFrames(layers[0].Icon!);
                        layer0Anchor = layers[0].Anchor;
                        layer0AnimationType = layers[0].Animation?.Type;
                        layer0AnimationDuration = layers[0].Animation?.Duration;
                        layer0MusicReactive = layers[0].Animation?.MusicReactive ?? false;
                        layer0Sensitivity = layers[0].Animation?.Sensitivity;
                    }
                    if (layers.Count > 1 && layers[1].Icon != null)
                    {
                        (layer1Frames, layer1Duration) = RenderIconFrames(layers[1].Icon!);
                        layer1Anchor = layers[1].Anchor;
                        layer1AnimationType = layers[1].Animation?.Type;
                        layer1AnimationDuration = layers[1].Animation?.Duration;
                        layer1MusicReactive = layers[1].Animation?.MusicReactive ?? false;
                        layer1Sensitivity = layers[1].Animation?.Sensitivity;
                    }
                }
            }
            else
            {
                _currentPalette = PixelLyric8BitFix.MobileSkinCatalog.Find(null);
            }
        }
        else
        {
            _currentPalette = PixelLyric8BitFix.MobileSkinCatalog.Find(skinId);
            if (PixelLyric8BitFix.MobileSkinIconCatalog.Find(skinId) is { } builtinIcon)
            {
                mainFrames = new[] { MobilePixelIconRenderer.Render(builtinIcon) };
            }
        }

        // 换皮肤/主题了：点击切姿势的状态、"连续听了多久"的计时都要归零——不能让新主题继续顶着
        // 上一个主题攒下来的"已经点到第几个动作"，也不能让上一个主题的自动切换阈值拿新主题的
        // 播放时长去比
        _baseIconFrames = mainFrames;
        _baseIconFrameDurationSeconds = mainFrameDurationSeconds;
        _iconActions = actions;
        _actionFrameBitmaps = actionFrames;
        _actionFrameDurations = actionDurations;
        _currentIconActionIndex = 0;
        _continuousPlaySeconds = 0;

        _mainIconAnim.Reset(_iconView, mainFrames, mainFrameDurationSeconds);
        _layer0Anim.Reset(_layer0View, layer0Frames, layer0Duration);
        _layer1Anim.Reset(_layer1View, layer1Frames, layer1Duration);

        // 简单招式动画（pulse/twinkle/sway/spin/flicker/bob）——主图标这份顺手记一份 type/duration，
        // 点击切姿势（CycleIconAction）时新姿势没自己配 animation 就落回这一份，见 ApplyCurrentIconActionFrame
        _themeIconAnimationType = mainAnimationType;
        _themeIconAnimationDuration = mainAnimationDuration;
        _themeIconMusicReactive = mainMusicReactive;
        _themeIconSensitivity = mainSensitivity;
        _mainIconMotion.Start(_iconView, mainAnimationType, mainAnimationDuration, mainMusicReactive, mainSensitivity);
        _layer0Motion.Start(_layer0View, layer0AnimationType, layer0AnimationDuration, layer0MusicReactive, layer0Sensitivity);
        _layer1Motion.Start(_layer1View, layer1AnimationType, layer1AnimationDuration, layer1MusicReactive, layer1Sensitivity);

        if (_overlayContainer == null || _overlayView == null) return;

        if (_overlayBackground == null)
        {
            float density = Resources?.DisplayMetrics?.Density ?? 3f;
            _strokeWidthPx = (int)(StrokeWidthDp * density);
            _overlayBackground = new GradientDrawable();
            _overlayBackground.SetShape(ShapeType.Rectangle);
            _overlayBackground.SetCornerRadius(CornerRadiusDp * density);
            _overlayContainer.Background = _overlayBackground;
        }

        _overlayBackground.SetColor(ToAndroidColor(_currentPalette.Background));
        _overlayBackground.SetStroke(_strokeWidthPx, ToAndroidColor(_currentPalette.Accent));
        _overlayView.SetTextColor(ToAndroidColor(_currentPalette.Text));

        if (_iconView != null) _iconView.Visibility = mainFrames is { Length: > 0 } ? ViewStates.Visible : ViewStates.Gone;
        ApplyLayerAnchor(_layer0View, layer0Frames, layer0Anchor);
        ApplyLayerAnchor(_layer1View, layer1Frames, layer1Anchor);
    }

    /// <summary>一份 CustomThemeIcon（主图标、某个装饰层、都是同一个形状）转成预渲染好的帧数组——
    /// 有 Frames 就只认 Frames、忽略 Rows（跟桌面版 CustomThemeIcon.Frames 的注释规则一样，1 帧当
    /// 静态图，>1 帧真的要循环播放），没有 Frames 才退回 Rows 当唯一一帧；两者都没有给 null，调用方
    /// 各自决定"没有图标"该怎么办（隐藏 ImageView）。</summary>
    private static (Bitmap[]? Frames, double DurationSeconds) RenderIconFrames(PixelLyric8BitFix.CustomThemeIcon icon)
    {
        var palette = PixelLyric8BitFix.CustomThemeValidator.BuildIconPalette(icon);
        if (icon.Frames is { Count: > 0 } frames)
        {
            var frameRows = frames.Select(f => f.ToArray()).ToList();
            return (MobilePixelIconRenderer.RenderFrames(frameRows, palette), PixelLyric8BitFix.CustomThemeValidator.GetFrameDurationSeconds(icon));
        }
        if (icon.Rows is { Count: > 0 } rows)
        {
            return (new[] { MobilePixelIconRenderer.Render(new PixelLyric8BitFix.MobileSkinIcon(rows.ToArray(), palette)) }, 0);
        }
        return (null, 0);
    }

    /// <summary>按 layer.anchor（四个角之一）摆放这个装饰层的 Gravity + 边距，没有图标/没配 anchor 就
    /// 整个隐藏。四个方向各自只设跟自己贴的那两条边的 margin（比如贴左上就只给 Left/Top），不然上一次
    /// 贴的是右下角、这次换成贴左上，没清掉的 Right/Bottom margin 会跟新的 Left/Top margin 一起生效，
    /// 图标被顶到不是任何一个角的奇怪位置。</summary>
    private void ApplyLayerAnchor(ImageView? view, Bitmap[]? frames, string? anchor)
    {
        if (view == null) return;
        if (frames is not { Length: > 0 } || anchor == null)
        {
            view.Visibility = ViewStates.Gone;
            return;
        }
        view.Visibility = ViewStates.Visible;
        if (view.LayoutParameters is not FrameLayout.LayoutParams lp) return;

        lp.LeftMargin = 0; lp.TopMargin = 0; lp.RightMargin = 0; lp.BottomMargin = 0;
        switch (anchor)
        {
            case "top-right":
                lp.Gravity = GravityFlags.Top | GravityFlags.Right;
                lp.RightMargin = _layerMarginPx; lp.TopMargin = _layerMarginPx;
                break;
            case "bottom-left":
                lp.Gravity = GravityFlags.Bottom | GravityFlags.Left;
                lp.LeftMargin = _layerMarginPx; lp.BottomMargin = _layerMarginPx;
                break;
            case "bottom-right":
                lp.Gravity = GravityFlags.Bottom | GravityFlags.Right;
                lp.RightMargin = _layerMarginPx; lp.BottomMargin = _layerMarginPx;
                break;
            default: // "top-left" 以及任何理论上不该出现的值（校验已经拦过），都落回左上角，不留一个摆不出去的装饰层
                lp.Gravity = GravityFlags.Top | GravityFlags.Left;
                lp.LeftMargin = _layerMarginPx; lp.TopMargin = _layerMarginPx;
                break;
        }
        view.LayoutParameters = lp; // 重新赋值触发 FrameLayout 按新 Gravity/margin 重新摆放
    }

    /// <summary>点一下装饰栏图标——循环切到下一个姿势，绕完一圈回到"动作 0"（主图标自己），跟桌面版
    /// MainWindow.SkinInteractions.cs 的 CustomIcon_MouseLeftButtonDown 是同一条规则。只换帧，不做
    /// 桌面版那个"同一条轨道才淡入淡出"的交叉淡化——Android 端还没有那一整套移动轨道的概念，见类顶部
    /// 注释，这版是瞬间切换。</summary>
    private void CycleIconAction()
    {
        if (_iconActions is not { Count: > 0 }) return;
        _currentIconActionIndex = (_currentIconActionIndex + 1) % (_iconActions.Count + 1); // 0 = 主图标本身
        ApplyCurrentIconActionFrame();
    }

    /// <summary>切到当前索引对应的那一份帧数组 + 那一份 animation——"动作 0"（主图标自己）永远用
    /// _themeIconAnimationType/_themeIconAnimationDuration（ApplySkin 时记的那份顶层 animation）；
    /// 别的动作如果自己填了 animation 就用自己那份，没填就落回顶层那份，跟桌面版 CustomThemeIconAction.
    /// Animation 的注释是同一条规则（"不填就是所有动作共用 icon 顶层那一个 animation"）。</summary>
    private void ApplyCurrentIconActionFrame()
    {
        if (_currentIconActionIndex <= 0)
        {
            _mainIconAnim.Reset(_iconView, _baseIconFrames, _baseIconFrameDurationSeconds);
            _mainIconMotion.Start(_iconView, _themeIconAnimationType, _themeIconAnimationDuration, _themeIconMusicReactive, _themeIconSensitivity);
            return;
        }

        int i = _currentIconActionIndex - 1;
        if (_actionFrameBitmaps != null && _actionFrameDurations != null && i >= 0 && i < _actionFrameBitmaps.Length)
        {
            _mainIconAnim.Reset(_iconView, _actionFrameBitmaps[i], _actionFrameDurations[i]);

            var actionAnimation = (_iconActions != null && i < _iconActions.Count) ? _iconActions[i].Animation : null;
            if (actionAnimation != null)
            {
                _mainIconMotion.Start(_iconView, actionAnimation.Type, actionAnimation.Duration, actionAnimation.MusicReactive, actionAnimation.Sensitivity);
            }
            else
            {
                _mainIconMotion.Start(_iconView, _themeIconAnimationType, _themeIconAnimationDuration, _themeIconMusicReactive, _themeIconSensitivity);
            }
        }
    }

    /// <summary>数据驱动的自动切换——当前这首歌"连续播放"满 CustomThemeIconAction.AutoSwitchAfterSeconds
    /// 秒就自动切到那个动作，不用等用户点，见 UpdateListeningStats 里顺手累加的 _continuousPlaySeconds。
    /// 多个动作都设了这个字段的话取"阈值已经被跨过的里面动作序号最大"的那个；已经手动点到（或者被
    /// 更高阶段自动切到）更靠后的动作时不会被拉回来，只会把索引往前推——跟桌面版 MainWindow.Skins.cs
    /// 的 EvaluateAutoSwitchIconAction 是同一条规则。</summary>
    private void EvaluateAutoSwitchIconAction()
    {
        if (_iconActions is not { Count: > 0 }) return;

        int target = _currentIconActionIndex;
        for (int i = 0; i < _iconActions.Count; i++)
        {
            if (_iconActions[i].AutoSwitchAfterSeconds is double threshold && threshold > 0 && _continuousPlaySeconds >= threshold)
            {
                int candidateIndex = i + 1; // 动作 0 是主图标，_iconActions[i] 对应索引 i+1
                if (candidateIndex > target) target = candidateIndex;
            }
        }
        if (target != _currentIconActionIndex)
        {
            _currentIconActionIndex = target;
            ApplyCurrentIconActionFrame();
        }
    }

    /// <summary>这次点击落点是不是在主图标的屏幕范围内——点在图标上循环切姿势，点在悬浮窗其它地方
    /// （文字/装饰层/空白）分享歌词，见 _rootFrame.Touch 的 Up 分支。图标当前隐藏（Visibility != Visible，
    /// 比如没有任何图标数据的皮肤）直接当"没点中"，不会点到一块看不见的区域却触发切换。</summary>
    private bool IsPointInsideIconView(float rawX, float rawY)
    {
        if (_iconView == null || _iconView.Visibility != ViewStates.Visible) return false;
        var location = new int[2];
        _iconView.GetLocationOnScreen(location);
        return rawX >= location[0] && rawX <= location[0] + _iconView.Width
            && rawY >= location[1] && rawY <= location[1] + _iconView.Height;
    }

    /// <summary>边框颜色的透明度按正弦波来回变化——呼吸感的淡入淡出，呼应桌面版自定义主题里
    /// "呼吸发光"那个内置动画的意思，但这边没有真的做一套动画系统，就是这一个效果，够精简版用了。
    /// 下限没设到全透明（90/255 起步），不然呼吸到最暗的时候看起来像边框"消失了一下"，不是变暗。</summary>
    private void PulseTick()
    {
        if (_overlayView == null || _overlayBackground == null) return;

        double elapsedSeconds = (DateTimeOffset.Now - _pulseStartTime).TotalSeconds;
        double wave = (Math.Sin(elapsedSeconds * Math.PI / 1.6) + 1.0) / 2.0; // 1.6 秒一个来回
        byte alpha = (byte)(90 + wave * 165);
        _overlayBackground.SetStroke(_strokeWidthPx,
            global::Android.Graphics.Color.Argb(alpha, _currentPalette.Accent.R, _currentPalette.Accent.G, _currentPalette.Accent.B));

        _mainHandler?.PostDelayed(_pulseAction!, PulseIntervalMs);
    }

    /// <summary>主图标 + 两个装饰层的逐帧播放——跟桌面版 MainWindow.Skins.cs 的
    /// UpdateCustomIconFrameAnimation 是同一个"tick 累计时间、攒够一帧的时长才切"思路，只是这边按
    /// 真实毫秒数累加（IconFrameTickIntervalMs 是固定的 tick 间隔），桌面版按 50ms 一次的固定 tick
    /// 数硬算，两种写法效果一样，这边这样写更顺手。三处各自的状态（帧数组/帧间隔/累计时间/当前帧号）
    /// 都收在 FrameAnimState 里，静态图标/没有图标（Frames 为 null 或只有 1 张）Tick 内部直接跳过，
    /// 但这个 tick 本身照样一直跑着（跟 _pulseAction 一样常驻），不然下次 ApplySkin 切到一个真的有
    /// 动画的主题时，还得再额外想办法把这个 tick 重新启动一遍。</summary>
    private void IconFrameTick()
    {
        _mainIconAnim.Tick(IconFrameTickIntervalMs);
        _layer0Anim.Tick(IconFrameTickIntervalMs);
        _layer1Anim.Tick(IconFrameTickIntervalMs);

        _mainHandler?.PostDelayed(_iconFrameAction!, IconFrameTickIntervalMs);
    }

    /// <summary>主图标 + 两个装饰层的"简单招式"动画（pulse/twinkle/sway/spin/flicker/bob）——每个 tick
    /// 按累计时间现算这一刻该是什么 Alpha/Rotation/TranslationY，直接改 View 属性，不是像
    /// IconFrameTick 那样"攒够时长才动一下"，这几招是连续渐变的，20fps（50ms 一次）才够顺滑。</summary>
    private void MotionTick()
    {
        _mainIconMotion.Tick(MotionTickIntervalMs);
        _layer0Motion.Tick(MotionTickIntervalMs);
        _layer1Motion.Tick(MotionTickIntervalMs);

        _mainHandler?.PostDelayed(_motionTickAction!, MotionTickIntervalMs);
    }

    /// <summary>客制化主题 animation 字段的 6 种"简单招式"——跟桌面版 MainWindow.Skins.cs 的
    /// StartCustomIconAnimation 是同一套参数（幅度/默认时长），桌面版是给每一招各自开一条 WPF
    /// Storyboard，这边没有 Storyboard，用 tick 手动算每一帧该是什么值，效果尽量对齐：
    /// pulse/twinkle/flicker 三招桌面版分别是"图标外发光的透明度"和"图标本体的透明度"两个不同的
    /// 视觉层，Android 端的 ImageView 没有现成的发光效果，这版统一收敛成图标本体的 Alpha，用不同的
    /// 幅度区间让三招看起来还是有区分度（不是完全一样的呼吸感）；sway/spin 都是 Rotation；bob 是
    /// TranslationY。drift/fall/walk 这三招桌面版要专属的渲染轨道（三重影飘过/飘落、装饰带里来回
    /// 走），这版还没做，选了这三招图标就静止不动，不报错也不崩——见类顶部注释这块已知的范围限制。
    ///
    /// type 支持用 "+" 连接的组合（比如 "pulse+sway"）——CustomThemeValidator.ValidateAnimation 已经
    /// 校验过组合本身是合法的（不会撞同一个 View 属性），这里直接信任传进来的数据，逐段拆开、各自
    /// 影响自己的属性，互不覆盖。</summary>
    private sealed class IconMotionState
    {
        private View? _target;
        private string[] _types = Array.Empty<string>();
        private double _durationSeconds;
        private double _elapsedMs;
        private bool _musicReactive;
        private double _sensitivityMultiplier = 1.0;

        public void Start(View? target, string? animationType, double? durationSeconds, bool musicReactive = false, string? sensitivity = null)
        {
            _target = target;
            _types = string.IsNullOrWhiteSpace(animationType)
                ? Array.Empty<string>()
                : animationType.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            _durationSeconds = durationSeconds is double d && d > 0 ? d : 0; // 0 = 没填，每一招用自己的默认时长
            _elapsedMs = 0;
            _musicReactive = musicReactive;
            _sensitivityMultiplier = PixelLyric8BitFix.CustomThemeValidator.SensitivityToMultiplier(sensitivity);

            // 换了动画（或者压根没有动画）就把上一套可能留下的变形复位，不然比如从 sway 切到没有
            // animation 的皮肤，图标会卡在上一次摆到一半的角度不回正
            if (_target != null)
            {
                _target.Alpha = 1f;
                _target.Rotation = 0f;
                _target.TranslationY = 0f;
            }
        }

        /// <summary>皮肤音乐律动——musicReactive 开着、总开关（MobileSettingsStore.MusicReactiveEnabled）
        /// 也开着、AudioReactiveCapture 真的在采集（IsActive）这三个条件都满足才会让这几招"跟着响度/
        /// 鼓点变速"，缺一个都退回正常速度（rate=1.0）播放，不是"没数据就随便定一个慢速"，见
        /// AudioReactiveCapture.cs 顶部注释。ratio 的原始值所有正在播的 animation 共用同一份，这里只
        /// 是套自己那份 sensitivity（跟桌面版 ApplyReactiveSensitivity 同一条公式），把"响度→速度"这条
        /// 曲线拉大/收窄，不是重新算一遍响度。</summary>
        public void Tick(int intervalMs)
        {
            if (_target == null || _types.Length == 0) return;

            double rate = 1.0;
            if (_musicReactive && MobileSettingsStore.MusicReactiveEnabled && AudioReactiveCapture.IsActive)
            {
                double raw = AudioReactiveCapture.RawRatio;
                double adjusted = AudioReactiveCapture.BaseSpeedRatio + (raw - AudioReactiveCapture.BaseSpeedRatio) * _sensitivityMultiplier;
                rate = Math.Clamp(adjusted, AudioReactiveCapture.MinSpeedRatio, AudioReactiveCapture.MaxSpeedRatio);
            }

            _elapsedMs += intervalMs * rate;
            double t = _elapsedMs / 1000.0;

            float? alpha = null, rotation = null, translationY = null;
            foreach (var type in _types)
            {
                switch (type)
                {
                    case "pulse": // 呼吸发光——幅度收窄一点（0.6~1.0），比 twinkle 更平缓
                        alpha = (float)Triangle(t, _durationSeconds > 0 ? _durationSeconds : 2.2, 0.6, 1.0);
                        break;
                    case "twinkle": // 渐隐渐现，幅度比 pulse 大，更像星星闪烁
                        alpha = (float)Triangle(t, _durationSeconds > 0 ? _durationSeconds : 1.6, 0.25, 1.0);
                        break;
                    case "flicker": // 不规则明暗跳动，节奏比 pulse/twinkle 更"毛躁"，见 Flicker
                        alpha = (float)Flicker(t, _durationSeconds > 0 ? _durationSeconds : 2.0);
                        break;
                    case "sway": // 以图标中心为轴心左右轻摆，±8°
                        rotation = (float)Triangle(t, _durationSeconds > 0 ? _durationSeconds : 3.2, -8, 8);
                        break;
                    case "spin": // 匀速转圈
                        {
                            double dur = _durationSeconds > 0 ? _durationSeconds : 4;
                            double frac = (t % dur) / dur;
                            rotation = (float)(frac * 360);
                        }
                        break;
                    case "bob": // 上下轻轻浮动，纯位置浮动不是旋转摆动（那是 sway）
                        {
                            double dur = _durationSeconds > 0 ? _durationSeconds : 3;
                            translationY = (float)(Math.Sin(t / dur * 2 * Math.PI) * 5);
                        }
                        break;
                    // drift/fall/walk：没有对应分支，静默跳过——图标继续静止不动，见类顶部注释
                }
            }

            if (alpha is float a) _target.Alpha = a;
            if (rotation is float r) _target.Rotation = r;
            if (translationY is float ty) _target.TranslationY = ty;
        }

        /// <summary>三角波：from 到 to 一个来回正好是 2×oneWayDurationSeconds——对应桌面版
        /// DoubleAnimation(from, to, oneWayDuration) 配 AutoReverse=true 的效果（那个 Duration
        /// 参数是"去程"一趟的时间，AutoReverse 会自动补一趟等长的回程）。</summary>
        private static double Triangle(double t, double oneWayDurationSeconds, double from, double to)
        {
            double full = oneWayDurationSeconds * 2;
            double phase = t % full;
            double frac = phase < oneWayDurationSeconds ? phase / oneWayDurationSeconds : (full - phase) / oneWayDurationSeconds; // 0→1→0
            return from + (to - from) * frac;
        }

        /// <summary>不规则明暗跳动——照抄桌面版 StartCustomIconAnimation 里 flicker 那几个关键帧的
        /// 时间比例，数值换算到这版收窄过的 Alpha 区间（0.55~0.85，原始是 drop shadow opacity 的
        /// 0.4~0.75，挪到图标本体上幅度收一点，免得跟主体一起变暗时太突兀）。</summary>
        private static double Flicker(double t, double durationSeconds)
        {
            double frac = (t % durationSeconds) / durationSeconds; // 0~1
            Span<(double X, double Y)> keys = stackalloc (double, double)[]
            {
                (0, 0.65), (0.15, 0.85), (0.3, 0.55), (0.42, 0.8), (1.0, 0.65),
            };
            for (int i = 0; i < keys.Length - 1; i++)
            {
                var (x0, y0) = keys[i];
                var (x1, y1) = keys[i + 1];
                if (frac >= x0 && frac <= x1)
                {
                    double localFrac = x1 > x0 ? (frac - x0) / (x1 - x0) : 0;
                    return y0 + (y1 - y0) * localFrac;
                }
            }
            return keys[^1].Y;
        }
    }

    /// <summary>逐帧动画的播放状态——主图标（含点击切姿势换上来的那一帧）、两个装饰层，三处都是同一套
    /// "tick 累计时间、攒够一帧的时长才切"逻辑，抽成这个小类避免三份重复的帧数组/帧间隔/累计时间/
    /// 当前帧号字段，见 IconFrameTick。Frames 只有 1 张（或 null）就是静态图标/没有图标，Tick 直接
    /// 跳过；Reset 换一套新数据的同时会立刻把第一帧画出来，不用等下一次 tick 才第一次显示。</summary>
    private sealed class FrameAnimState
    {
        private Bitmap[]? _frames;
        private double _durationSeconds;
        private double _elapsedMs;
        private int _index;
        private ImageView? _target;

        public void Reset(ImageView? target, Bitmap[]? frames, double durationSeconds)
        {
            _target = target;
            _frames = frames;
            _durationSeconds = durationSeconds;
            _elapsedMs = 0;
            _index = 0;
            if (_target != null && _frames is { Length: > 0 })
            {
                _target.SetImageBitmap(_frames[0]);
            }
        }

        public void Tick(int intervalMs)
        {
            if (_target == null || _frames is not { Length: > 1 }) return;
            _elapsedMs += intervalMs;
            double frameDurationMs = Math.Max(50, _durationSeconds * 1000); // 下限保护，免得 JSON 填了个离谱小的值导致每 tick 都切帧
            if (_elapsedMs >= frameDurationMs)
            {
                _elapsedMs = 0;
                _index = (_index + 1) % _frames.Length;
                _target.SetImageBitmap(_frames[_index]);
            }
        }
    }

    private static global::Android.Graphics.Color ToAndroidColor(PixelLyric8BitFix.RgbaColor c) =>
        global::Android.Graphics.Color.Argb(c.A, c.R, c.G, c.B);

    /// <summary>SharedPreferences 一变（不管改的是哪一项，见 SettingsPrefListener）就整个重新套一遍：
    /// 换皮肤、同步偏移、卡拉OK 开关都是直接读新值就行；双语开关单独处理——刚被打开的话，当前这首歌
    /// 如果已经有歌词但还没翻译过，要顺手补一次（缓存命中就直接用，没有才现场翻），不然用户开了开关
    /// 却要等切下一首歌才第一次看到翻译。</summary>
    private void RefreshSettingsFromStore()
    {
        ApplySkin(MobileSettingsStore.SelectedSkinId);
        _syncOffsetMs = MobileSettingsStore.SyncOffsetMs;
        _karaokeEnabled = MobileSettingsStore.KaraokeEnabled;

        bool bilingualNowEnabled = MobileSettingsStore.BilingualEnabled;
        if (bilingualNowEnabled && !_bilingualEnabled)
        {
            TryEnsureTranslationForCurrentTrack();
        }
        else if (!bilingualNowEnabled)
        {
            _translationLines = null; // 关掉双语——不用清缓存，只是这次不显示了，下次再开还能直接命中缓存
        }
        _bilingualEnabled = bilingualNowEnabled;

        UpdateOverlayText();
    }

    // ── 媒体会话订阅：活跃会话列表变化 + 当前会话的播放状态/元数据变化，两层事件都要订 ──────────

    private bool _activeSessionsListenerRegistered;

    private void BindMediaSessionManager()
    {
        _sessionManager = (AndroidMediaSession.MediaSessionManager)GetSystemService(MediaSessionService)!;
        _activeSessionsListener = new ActiveSessionsListener(RebindController);
        TryRegisterActiveSessionsListener();

        // listener 只会在"以后列表变了"的时候推——这里先手动查一次现在已经在播的，不然要等下一次
        // 切歌/换 App 才会第一次显示出东西
        RebindController(MediaNotificationListenerService.Instance?.GetActiveMediaControllers());
    }

    /// <summary>真正订阅"活跃会话列表变化"广播——这一步要求调用方已经拿到"通知使用权"（见
    /// PermissionsPage），没拿到的话系统会直接抛 SecurityException（不是返回 false/null 这种能提前
    /// 判断的信号）。真机踩过的坑：用户先点了"显示悬浮窗"、还没去开通知使用权，这个异常会一路冒出
    /// Service.OnStartCommand 没人接，把整个 App 进程带崩——用户体感就是"悬浮窗点了没反应、App 卡死
    /// 出不来"，得靠系统"应用无响应"强制关闭才能脱身，见 OnStartCommand 的兜底 catch。这里先查一遍
    /// 权限、真的抛了也接住，悬浮窗照样能正常显示出来（只是暂时读不到"现在在播什么"），不会因为这
    /// 一步崩掉整个前台服务。RetryBindControllerIfMissing 每隔几秒会再调一次这个方法，用户去设置里
    /// 把通知使用权补上之后，不用重开一次悬浮窗，自己就能重新连上。</summary>
    private void TryRegisterActiveSessionsListener()
    {
        if (_activeSessionsListenerRegistered || _sessionManager == null || _activeSessionsListener == null) return;
        if (!MediaNotificationListenerService.IsListenerAccessGranted(this)) return;

        try
        {
            var component = new ComponentName(this, Java.Lang.Class.FromType(typeof(MediaNotificationListenerService)));
            _sessionManager.AddOnActiveSessionsChangedListener(_activeSessionsListener, component);
            _activeSessionsListenerRegistered = true;
        }
        catch (Exception ex)
        {
            // 权限查询（IsListenerAccessGranted）和系统实际校验之间偶尔会有一拍时间差（刚开权限那一
            // 瞬间）——接住就行，下一次 RetryBindControllerIfMissing 的 tick 会再试一次，不影响悬浮窗
            // 本身正常显示
            global::Android.Util.Log.Warn("ZipPlayOverlay", "AddOnActiveSessionsChangedListener failed: " + ex);
        }
    }

    /// <summary>活跃会话列表变了（新 App 开始播/原来那个会话没了）——挑第一个当"现在显示这个"，
    /// 换绑 MediaController.Callback。同一个会话没变的话什么都不用做，回调本身会推更新过来。</summary>
    private void RebindController(IList<AndroidMediaSession.MediaController>? controllers)
    {
        var next = controllers != null && controllers.Count > 0 ? controllers[0] : null;

        bool sameSession = _controller != null && next != null
            && Equals(_controller.SessionToken, next.SessionToken);
        if (sameSession) return;

        FlushListeningStats(_currentTitle + "|" + _currentArtist); // 会话要换了，把上一个会话攒的听歌时长零头先记上，不然会跟着下面的重置一起丢掉

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
            UpdateOverlayText();
            return;
        }

        _controllerCallback = new PlaybackCallback(this);
        _controller.RegisterCallback(_controllerCallback, _mainHandler);

        // RegisterCallback 只保证"以后变化"会推过来，这个会话当前已经在报的状态得自己先问一次，
        // 不然要等下一次播放状态/元数据变化才会第一次显示出东西
        HandleMetadataChanged(_controller.Metadata);
        HandlePlaybackStateChanged(_controller.PlaybackState);
    }

    // ── MediaController.Callback 推过来的两类事件 ───────────────────────────────────

    private void HandleMetadataChanged(MediaMetadata? metadata)
    {
        string previousTrackKey = _currentTitle + "|" + _currentArtist;

        _currentTitle = metadata?.GetString(MediaMetadata.MetadataKeyTitle) ?? "（无标题）";
        _currentArtist = metadata?.GetString(MediaMetadata.MetadataKeyArtist) ?? "";
        long durationMs = metadata?.GetLong(MediaMetadata.MetadataKeyDuration) ?? 0;
        _totalDuration = durationMs > 0 ? TimeSpan.FromMilliseconds(durationMs) : TimeSpan.Zero;

        string trackKey = _currentTitle + "|" + _currentArtist;
        if (trackKey != _fetchedForTrackKey && !_fetchInFlight)
        {
            FlushListeningStats(previousTrackKey); // 换歌了，把上一首歌攒的零头先记上，不然会被记到新歌头上
            _fetchedForTrackKey = trackKey;
            _currentLines = null;
            _currentLrcContent = null;
            _translationLines = null;
            _continuousPlaySeconds = 0; // 换歌清零——点击切姿势的自动切换阈值是按"这首歌连续播放了多久"算的，见 EvaluateAutoSwitchIconAction
            _fetchCts?.Cancel();
            _fetchCts = new System.Threading.CancellationTokenSource();
            _ = FetchLyricsAsync(_currentTitle, _currentArtist, _totalDuration, _fetchCts.Token);
        }

        UpdateOverlayText();
    }

    private void HandlePlaybackStateChanged(AndroidMediaSession.PlaybackState? state)
    {
        // 跟桌面版 RefreshAnchor 是同一个思路：把"系统汇报的位置"当锚点，配上"收到这次汇报的本地时间"，
        // 之后靠 PlaybackPositionEstimator 纯本地插值，不用每次画面刷新都再问系统一次
        _anchorPosition = TimeSpan.FromMilliseconds(state?.Position ?? 0);
        _anchorTime = DateTimeOffset.Now;
        _isPlaying = state?.State == AndroidMediaSession.PlaybackStateCode.Playing;
        _playbackRate = (state?.PlaybackSpeed is float r && r > 0) ? r : 1.0;

        UpdateOverlayText();
    }

    private void HandleControllerSessionDestroyed()
    {
        // 会话彻底没了——AddOnActiveSessionsChangedListener 通常也会紧跟着推一次新列表过来，但保险起见
        // 这里先自己把状态清掉，不然要等那次回调之前，悬浮窗会一直停在这首歌最后一句歌词不动
        if (_controller != null && _controllerCallback != null)
        {
            _controller.UnregisterCallback(_controllerCallback);
        }
        _controller = null;
        _controllerCallback = null;
        UpdateOverlayText();
    }

    // ── 本地渲染 tick：只用已知锚点插值，不碰任何系统 API ──────────────────────────────

    private void RenderTick()
    {
        if (_overlayView == null) return; // 悬浮窗已经被关掉了，不用再排下一次

        RetryBindControllerIfMissing();
        UpdateOverlayText();
        UpdateListeningStats(); // 复用这个 tick 顺手攒听歌时长，不用再单独开一个定时器
        _mainHandler?.PostDelayed(_renderAction!, RenderIntervalMs);
    }

    // 绑定媒体会话本来只在 BindMediaSessionManager 里查一次，之后全靠"活跃会话列表变化"这个系统
    // 广播（ActiveSessionsListener）推更新——真机上踩过这个坑：悬浮窗刚启动那一刻
    // MediaNotificationListenerService.Instance 恰好还没连接上（服务重启/系统刚杀过又重连这类情况，
    // 不一定是权限问题），那一次 RebindController(null) 就把 _controller 定死成 null，之后哪怕
    // 服务真的连上了，只要活跃会话列表本身没有再变化过（比如用户开始播放前会话就已经存在），
    // 就永远没有下一次机会重新查——用户会一直看到"没有检测到正在播放的 App"，得自己去系统设置
    // 把权限关了再开一次才能救回来。这里顶个每隔几秒重试一次的兜底：只要还没绑定到任何会话，
    // 就重新问一遍，服务晚连上了也能自愈，不用整套指望系统那个"列表变化"广播。
    private const int ControllerRebindRetryIntervalMs = 3000;
    private DateTimeOffset _lastControllerRebindAttempt = DateTimeOffset.MinValue;

    private void RetryBindControllerIfMissing()
    {
        if (_controller != null) return;
        if ((DateTimeOffset.Now - _lastControllerRebindAttempt).TotalMilliseconds < ControllerRebindRetryIntervalMs) return;

        _lastControllerRebindAttempt = DateTimeOffset.Now;
        TryRegisterActiveSessionsListener(); // 没注册成功过的话顺手再试一次，见该方法顶部注释——权限是这次才补上的话，不用重开悬浮窗就能自愈
        RebindController(MediaNotificationListenerService.Instance?.GetActiveMediaControllers());
    }

    // ── 听歌统计 + 成就 ───────────────────────────────────────────────────────────

    /// <summary>按真实墙钟时间攒进 _pendingListenSeconds（不是按 _playbackRate 放大，跟桌面版
    /// UpdateListeningStats 是同一个口径），攒够阈值才 flush 进存档，不是每个 200ms tick 都写盘。</summary>
    private void UpdateListeningStats()
    {
        var now = DateTimeOffset.Now;
        double elapsed = (now - _lastStatsTickTime).TotalSeconds;
        _lastStatsTickTime = now;

        if (_isPlaying && _controller != null && !string.IsNullOrEmpty(_currentTitle) && elapsed > 0 && elapsed < StatsMaxTickGapSeconds)
        {
            _pendingListenSeconds += elapsed;

            // 点击切姿势的数据驱动自动切换（AutoSwitchAfterSeconds）用的"这首歌连续播放了多久"，
            // 复用同一段"暂停不计时、tick 间隔太长不计入"的口径，不用单独再攒一份，见类顶部注释
            _continuousPlaySeconds += elapsed;
            EvaluateAutoSwitchIconAction();
        }

        if (_pendingListenSeconds >= StatsFlushThresholdSeconds)
        {
            FlushListeningStats(_currentTitle + "|" + _currentArtist);
        }
    }

    /// <summary>把 _pendingListenSeconds 记到 trackKey 名下的"今天"这一桶里存盘，然后评估一遍 8 个成就，
    /// 刚解锁的弹个 Toast。trackKey 传的是"这段时长归属哪首歌"——大多数情况下就是当前这首，但换歌/
    /// 换会话/悬浮窗关闭这几个时机要传*切换前*的那个 trackKey，不然这几秒会被记到新歌头上，
    /// 见各调用点。</summary>
    private void FlushListeningStats(string trackKey)
    {
        if (_pendingListenSeconds < 1 || trackKey == "|")
        {
            _pendingListenSeconds = 0;
            return;
        }

        int seconds = (int)Math.Round(_pendingListenSeconds);
        _pendingListenSeconds = 0;
        if (seconds <= 0) return;

        string dayKey = DateTime.Now.ToString("yyyy-MM-dd");
        if (!_stats.Days.TryGetValue(dayKey, out var day))
        {
            day = new PixelLyric8BitFix.DayStats();
            _stats.Days[dayKey] = day;
        }
        day.TotalSeconds += seconds;
        day.TrackSeconds[trackKey] = day.TrackSeconds.GetValueOrDefault(trackKey) + seconds;

        // 干净标题/艺人给统计报告显示用——去掉 remix/feat 这类标注，跟桌面版 MainWindow.Lyrics.cs 里
        // 抓词前那一步是同一份清洗规则，不是重新发明一套；trackKey 本身仍然是原始未清洗的，
        // 跟歌词缓存那边"这是同一首歌"的判断标准保持一致
        int sep = trackKey.IndexOf('|');
        string rawTitle = sep >= 0 ? trackKey[..sep] : trackKey;
        string rawArtist = sep >= 0 ? trackKey[(sep + 1)..] : "";
        string cleanTitle = System.Text.RegularExpressions.Regex.Replace(rawTitle,
            @"\s*[\(\[][^\]\)]*(feat|with|remix|version|prod)[^\]\)]*[\)\]]", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
        string cleanArtist = rawArtist.Split(new[] { ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? rawArtist;
        _stats.Tracks[trackKey] = new PixelLyric8BitFix.TrackInfo { Title = cleanTitle, Artist = cleanArtist };

        _statsStore?.Save(_stats);
        CelebrateNewlyUnlockedAchievements();
    }

    private void CelebrateNewlyUnlockedAchievements()
    {
        if (_unlockTracker == null) return;

        var progress = PixelLyric8BitFix.AchievementCalculator.Evaluate(_stats);
        var newlyUnlocked = _unlockTracker.DetectNewlyUnlocked(progress);
        if (newlyUnlocked.Count == 0) return;

        string names = string.Join("、", newlyUnlocked.Select(a => $"{a.Icon} {a.Name}"));
        Toast.MakeText(this, $"🎉 成就解锁：{names}", ToastLength.Long)?.Show();
    }

    private void UpdateOverlayText()
    {
        if (_controller == null)
        {
            _currentLyricLine = null; // 没有正在播放的 App，没有"正在显示的歌词"这回事，点一下分享要能立刻发现这点
            SetOverlayText("ZipPlay 悬浮歌词骨架\n（现在没有检测到正在播放的 App）");
            return;
        }

        if (_currentLines == null)
        {
            _currentLyricLine = null; // 还没抓到词/抓词失败，同上
            SetOverlayText(_fetchInFlight
                ? $"{_currentTitle} - {_currentArtist}\n（正在联网找歌词…）"
                : $"{_currentTitle} - {_currentArtist}\n（没找到歌词）");
            return;
        }

        var position = PixelLyric8BitFix.PlaybackPositionEstimator.Estimate(
            _anchorPosition, _anchorTime, DateTimeOffset.Now, _isPlaying, _playbackRate, _totalDuration);
        // 加上同步偏移再去找该显示哪一行——只影响"挑哪一行歌词"，不影响真实播放位置本身，
        // 跟桌面版鼠标滚轮微调是同一个意思（偏移量的正负号约定也一样：正数让歌词更晚出现）
        int positionMs = (int)position.TotalMilliseconds + _syncOffsetMs;

        int currentIndex = -1;
        for (int i = 0; i < _currentLines.Count; i++)
        {
            if (_currentLines[i].TimeMs > positionMs) break;
            currentIndex = i;
        }

        if (currentIndex < 0)
        {
            _currentLyricLine = null; // 歌词抓到了，但播放位置还没到第一行的时间戳，同上
            SetOverlayText($"{_currentTitle} - {_currentArtist}");
            return;
        }

        var (lineStartMs, lineText) = _currentLines[currentIndex];
        _currentLyricLine = lineText; // 分享卡片要的就是这一行原文，不带卡拉OK 上色/双语翻译那些展示层的东西
        // 这一行唱到什么时候算完——下一行开始的时间，或者（最后一行）总时长；两个都拿不到就兜底
        // 留 4 秒，跟桌面版骨架示例数据那个"最后一句放完留 4 秒"是同一个数字，没有特殊含义只是个保守值
        int lineEndMs = currentIndex + 1 < _currentLines.Count
            ? _currentLines[currentIndex + 1].TimeMs
            : (_totalDuration > TimeSpan.Zero ? (int)_totalDuration.TotalMilliseconds : lineStartMs + 4000);

        int sungChars = PixelLyric8BitFix.KaraokeTiming.EstimateSungChars(lineText, positionMs, lineStartMs, lineEndMs);

        string? translationText = null;
        if (_translationLines != null)
        {
            foreach (var (timeMs, text) in _translationLines)
            {
                if (timeMs > positionMs) break;
                translationText = text;
            }
        }

        SetOverlayLyricLine(lineText, sungChars, translationText);
    }

    // ── 🖼️ 歌词分享卡片 ───────────────────────────────────────────────────────────
    // 触发点见 ShowOverlay 里的 Touch 手势——点一下悬浮窗（不是拖）就是这个，跟桌面版右键菜单点
    // "截成图片"是同一个功能，只是手机上没有右键菜单，借用"点一下"当入口。

    /// <summary>把 _currentLyricLine 画成一张图存进系统相册，再弹系统分享面板——没有正在显示的歌词
    /// （没在放歌/还没抓到词/播放位置还没到第一行）就提示一句，不弹一张空卡片出来。</summary>
    private void ShareCurrentLyricLine()
    {
        if (string.IsNullOrEmpty(_currentLyricLine))
        {
            Toast.MakeText(this, "现在没有正在显示的歌词，放着歌再点一下悬浮窗试试", ToastLength.Short)?.Show();
            return;
        }

        try
        {
            // 直接拿 _iconView 这一刻真的在显示的那张位图——跟悬浮窗上看到的是同一张（哪怕 icon.frames
            // 逐帧动画正播到中间某一帧），不重新渲染一遍，也不用另外记一份"当前是第几帧"
            Bitmap? icon = (_iconView?.Drawable as BitmapDrawable)?.Bitmap;
            var card = LyricShareCardRenderer.Render(_currentLyricLine, _currentTitle, _currentArtist, _currentPalette, icon);

            // 不用 MediaStore.Images.Media.InsertImage——那个旧 API 内部固定把图压成 JPEG 质量 50，
            // 像素图标的硬边缘/文字会被压出可见的模糊和色块，跟这份卡片"像素画不做模糊处理"的
            // 一贯做法直接冲突。手动插入一条 MediaStore 记录再用 Bitmap.Compress(Png, 100) 写进去，
            // 才是真的无损 PNG，也让下面 Intent 里声明的 "image/png" 名副其实（用 InsertImage 那版
            // 声明是 png、实际存的是 jpg，类型对不上）
            var values = new ContentValues();
            values.Put("_display_name", $"ZipPlay_lyric_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.png");
            values.Put("mime_type", "image/png");
            var uri = ContentResolver!.Insert(MediaStore.Images.Media.ExternalContentUri!, values);
            if (uri == null)
            {
                Toast.MakeText(this, "生成分享图失败，稍后再试试", ToastLength.Short)?.Show();
                return;
            }
            using (var stream = ContentResolver.OpenOutputStream(uri))
            {
                card.Compress(Bitmap.CompressFormat.Png!, 100, stream);
            }

            var shareIntent = new Intent(Intent.ActionSend);
            shareIntent.SetType("image/png");
            shareIntent.PutExtra(Intent.ExtraStream, uri);
            shareIntent.AddFlags(ActivityFlags.GrantReadUriPermission);

            // 从 Service（不是 Activity）发 Intent 必须带 NewTask，不然系统直接拒绝——这是 Android
            // 的硬性要求，不是我们自己想加的
            var chooser = Intent.CreateChooser(shareIntent, "分享歌词")!;
            chooser.AddFlags(ActivityFlags.NewTask);
            StartActivity(chooser);
        }
        catch (Exception ex)
        {
            Toast.MakeText(this, "分享失败：" + ex.Message, ToastLength.Short)?.Show();
        }
    }

    /// <summary>真正碰 TextView 之前先比一下"这次要显示的东西"跟上次是不是一样——大部分 200ms tick 里
    /// 歌词/翻译/唱到第几个字都没变，没必要每次都触发一次 TextView 的重新排版/重绘。</summary>
    private void SetOverlayText(string text)
    {
        if (_overlayView == null || _lastRenderedText == text) return;
        _lastRenderedText = text;
        _overlayView.SetText(text, TextView.BufferType.Normal); // 切回普通文本——上一次如果是卡拉OK 的 Spannable，不清掉样式会一直带着
    }

    /// <summary>显示一句真正在同步的歌词——开了卡拉OK 的话用 SpannableString 给"已经唱过的字"和
    /// "还没唱到的字"上两级颜色（唱过的用正常文字色，没唱到的调暗），呼应桌面版卡拉OK 扫光的视觉
    /// 语言，只是这边没法做逐字的渐变过渡，只有"唱了/没唱"两级，够精简版用了。双语开着且这一行有
    /// 翻译的话，翻译另起一行接在后面（换行不影响卡拉OK 的字符范围，因为范围只算在原文这一段里）。</summary>
    private void SetOverlayLyricLine(string lineText, int sungChars, string? translationText)
    {
        if (_overlayView == null) return;

        string dedupeKey = $"{lineText}|{sungChars}|{translationText}|{_karaokeEnabled}";
        if (_lastRenderedText == dedupeKey) return;
        _lastRenderedText = dedupeKey;

        string full = translationText != null ? lineText + "\n" + translationText : lineText;

        if (_karaokeEnabled && sungChars > 0 && sungChars < lineText.Length)
        {
            var spannable = new SpannableString(full);
            spannable.SetSpan(new ForegroundColorSpan(ToAndroidColor(_currentPalette.Text)), 0, sungChars, SpanTypes.ExclusiveExclusive);
            spannable.SetSpan(new ForegroundColorSpan(DimColor(_currentPalette.Text)), sungChars, lineText.Length, SpanTypes.ExclusiveExclusive);
            _overlayView.SetText(spannable, TextView.BufferType.Spannable);
        }
        else
        {
            _overlayView.SetText(full, TextView.BufferType.Normal);
        }
    }

    /// <summary>卡拉OK"还没唱到"那部分文字的颜色——直接把 alpha 减半，看起来像调暗了一截，不是
    /// 换了个颜色，这样任何皮肤配色都能直接用同一个公式，不用给每套皮肤单独配一个"暗调"色。</summary>
    private static global::Android.Graphics.Color DimColor(PixelLyric8BitFix.RgbaColor c) =>
        global::Android.Graphics.Color.Argb((byte)(c.A / 2), c.R, c.G, c.B);

    private async System.Threading.Tasks.Task FetchLyricsAsync(string title, string artist, TimeSpan expectedDuration, System.Threading.CancellationToken token)
    {
        _fetchInFlight = true;
        string trackKey = title + "|" + artist;
        try
        {
            // 本地缓存先查一次——命中就直接用，跳过整个联网抓词流程，同一首歌下次再放/来回切歌
            // 切回来直接秒出，不用重新走一遍多引擎并发请求
            string? lrcContent = _lyricsCache?.TryGet(trackKey);
            if (lrcContent == null)
            {
                var result = await _lyricsFetcher!.FetchAsync(title, artist, expectedDuration, token);
                if (token.IsCancellationRequested) return; // 抓词这几秒里歌又换了，这份结果作废

                lrcContent = result?.Lrc;
                if (!string.IsNullOrEmpty(lrcContent)) _lyricsCache?.Save(trackKey, lrcContent);
            }

            _currentLrcContent = lrcContent;
            _currentLines = !string.IsNullOrEmpty(lrcContent)
                ? PixelLyric8BitFix.LrcParser.ParseLines(lrcContent)
                : new List<(int TimeMs, string Text)>(); // 空列表当"确实找不到"，跟"还没抓完"（null）区分开

            if (_bilingualEnabled) TryEnsureTranslationForCurrentTrack();
        }
        catch
        {
            // 联网失败/超时——悬浮窗继续显示"没找到歌词"就行，不需要弹错误吓用户
            _currentLines = new List<(int TimeMs, string Text)>();
        }
        finally
        {
            _fetchInFlight = false;
        }
        UpdateOverlayText();
    }

    /// <summary>当前这首歌需要翻译的话补一次——缓存命中直接用，没有才现场翻。两个调用点：歌刚抓完
    /// （FetchLyricsAsync）、双语开关刚被打开（RefreshSettingsFromStore），逻辑完全一样所以抽成一个
    /// 方法，不是各写一遍。</summary>
    private void TryEnsureTranslationForCurrentTrack()
    {
        if (string.IsNullOrEmpty(_currentLrcContent) || _translationLines != null) return;

        string trackKey = _currentTitle + "|" + _currentArtist;
        string? cachedTranslation = _lyricsCache?.TryGetTranslation(trackKey);
        if (cachedTranslation != null)
        {
            _translationLines = PixelLyric8BitFix.LrcParser.ParseLines(cachedTranslation);
            return;
        }

        _ = TranslateAsync(trackKey, _currentLrcContent, _fetchCts?.Token ?? System.Threading.CancellationToken.None);
    }

    private async System.Threading.Tasks.Task TranslateAsync(string trackKey, string lrcContent, System.Threading.CancellationToken token)
    {
        try
        {
            string? translated = await _translator!.TranslateLrcAsync(lrcContent, token);
            if (token.IsCancellationRequested || translated == null) return;

            _lyricsCache?.SaveTranslation(trackKey, translated);

            // 翻这几秒歌又换了的话，这份结果只存缓存（下次播到这首歌就能直接命中），不显示到已经
            // 过期的界面上——不然会出现"当前这句歌词是新歌，下面那行翻译却是上一首的"这种错位
            if (trackKey == _currentTitle + "|" + _currentArtist)
            {
                _translationLines = PixelLyric8BitFix.LrcParser.ParseLines(translated);
                UpdateOverlayText();
            }
        }
        catch
        {
            // 翻译失败不影响原文正常显示，静默放弃就行
        }
    }

    private void RemoveOverlay()
    {
        FlushListeningStats(_currentTitle + "|" + _currentArtist); // 悬浮窗要关了，不丢最后几秒听歌时长，跟桌面版关窗时的 flush 是同一个用意

        // 皮肤音乐律动——悬浮窗都关了就没必要继续占着 AudioRecord/MediaProjection 这些系统资源，
        // 也顺手把 Android 15 那个"正在被捕获"的状态栏提示收掉，不留着白占，见 AudioReactiveCapture.
        // Stop 注释
        AudioReactiveCapture.ActiveChanged -= RefreshForegroundServiceType;
        AudioReactiveCapture.Stop();

        if (_mainHandler != null && _renderAction != null)
        {
            _mainHandler.RemoveCallbacks(_renderAction);
        }
        _renderAction = null;

        if (_mainHandler != null && _pulseAction != null)
        {
            _mainHandler.RemoveCallbacks(_pulseAction);
        }
        _pulseAction = null;
        _overlayBackground = null;

        if (_mainHandler != null && _iconFrameAction != null)
        {
            _mainHandler.RemoveCallbacks(_iconFrameAction);
        }
        _iconFrameAction = null;
        _baseIconFrames = null;
        _iconActions = null;
        _actionFrameBitmaps = null;
        _actionFrameDurations = null;
        _currentIconActionIndex = 0;
        _continuousPlaySeconds = 0;

        if (_mainHandler != null && _motionTickAction != null)
        {
            _mainHandler.RemoveCallbacks(_motionTickAction);
        }
        _motionTickAction = null;

        if (_settingsPrefListener != null)
        {
            MobileSettingsStore.UnregisterChangeListener(_settingsPrefListener);
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

        _mainHandler = null;

        if (_rootFrame != null && _windowManager != null)
        {
            _windowManager.RemoveView(_rootFrame);
        }
        _rootFrame = null;
        _overlayContainer = null;
        _iconView = null;
        _overlayView = null;
        _layer0View = null;
        _layer1View = null;
        _layoutParams = null;
    }

    public static void Start(Context context)
    {
        var intent = new Intent(context, typeof(FloatingOverlayService));
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            context.StartForegroundService(intent);
        }
        else
        {
            context.StartService(intent);
        }
    }

    public static void Stop(Context context) =>
        context.StopService(new Intent(context, typeof(FloatingOverlayService)));

    /// <summary>悬浮窗权限是不是已经开了——Android 6 (API 23) 之前这个权限装完就自动有，不用查。</summary>
    public static bool CanDrawOverlays(Context context) =>
        Build.VERSION.SdkInt < BuildVersionCodes.M || Settings.CanDrawOverlays(context);

    /// <summary>跳去系统设置的"显示在其他应用上层"权限页——特殊权限，没法用普通运行时权限弹窗申请，
    /// 必须让用户自己去设置里手动开。</summary>
    public static void OpenOverlaySettings(Context context)
    {
        var intent = new Intent(Settings.ActionManageOverlayPermission,
            global::Android.Net.Uri.Parse("package:" + context.PackageName));
        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }

    /// <summary>MediaSessionManager.OnActiveSessionsChangedListener 这个 Java 接口的最简单实现——
    /// 就一个方法，没必要为了实现它专门去继承什么，包一层委托转发给外面就行。</summary>
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
        private readonly FloatingOverlayService _owner;

        public PlaybackCallback(FloatingOverlayService owner) => _owner = owner;

        public override void OnPlaybackStateChanged(AndroidMediaSession.PlaybackState? state) => _owner.HandlePlaybackStateChanged(state);

        public override void OnMetadataChanged(MediaMetadata? metadata) => _owner.HandleMetadataChanged(metadata);

        public override void OnSessionDestroyed() => _owner.HandleControllerSessionDestroyed();
    }

    /// <summary>SharedPreferences.OnSharedPreferenceChangeListener 的最简单实现——同一个套路，
    /// 就一个方法，包一层委托转发给外面，不用为了实现它专门去继承什么。key 是哪个设置项变了这里
    /// 用不上，皮肤/偏移/卡拉OK/双语哪个设置一变就整个重新套一遍（见 RefreshSettingsFromStore），
    /// 不用挑着改。</summary>
    private sealed class SettingsPrefListener : Java.Lang.Object, ISharedPreferencesOnSharedPreferenceChangeListener
    {
        private readonly Action _onChanged;

        public SettingsPrefListener(Action onChanged) => _onChanged = onChanged;

        public void OnSharedPreferenceChanged(ISharedPreferences? sharedPreferences, string? key) => _onChanged();
    }
}
