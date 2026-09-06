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
/// frameDuration 循环播放（见 IconFrameTick）——多层装饰/点击切姿势/专属律动动画这些桌面版才有的
/// 完整皮肤系统还没有，那套留给以后真要做的时候。
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
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeSpecialUse)]
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
    private LinearLayout? _overlayContainer; // 真正加进 WindowManager、参与拖动的是这一个，图标 + 文字都是它的子 View
    private ImageView? _iconView;
    private TextView? _overlayView;
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
    private Bitmap[]? _iconFrameBitmaps; // null 或者只有 1 张就是静态图标，IconFrameTick 直接跳过
    private double _iconFrameDurationSeconds;
    private double _iconFrameElapsedMs; // 累计经过的时间，攒够一帧的时长才真的切，不是每个 tick 都切
    private int _iconFrameIndex;
    private Action? _iconFrameAction;

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

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        StartForegroundWithNotification();
        ShowOverlay();
        IsRunning = true;
        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        RemoveOverlay();
        _fetchCts?.Cancel();
        IsRunning = false;
        base.OnDestroy();
    }

    // Android 8 (API 26) 起，前台服务必须配一条常驻通知——这是系统强制的，不是我们自己想加的，
    // 用户会在通知栏看到"ZipPlay 悬浮歌词正在运行"这样一条提示，这也是让用户知道"这个悬浮窗
    // 是怎么冒出来的、想关掉去哪关"的正常渠道。
    private void StartForegroundWithNotification()
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
        // ForegroundServiceType 一起，两边都要对得上，缺一个都会在 API 34+ 的设备上直接崩
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
        {
            StartForeground(NotificationId, notification, ForegroundService.TypeSpecialUse);
        }
        else
        {
            StartForeground(NotificationId, notification);
        }
    }

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
        // LayoutParams 对象最后一次 UpdateViewLayout 传的位置，不用自己再存一份。
        //
        // 顺手在这上面加了"点一下（不是拖）分享当前这句歌词"——按下到松手之间移动距离没超过
        // tapMoveThresholdPx 就算一次单纯的点击，不是拖动，见 MotionEventActions.Up；跟拖动共用同一个
        // 手势识别器而不是单独挂一个 Click，是因为悬浮窗一直在拦截触摸事件做拖动，另外挂的 Click
        // 永远收不到事件
        float touchStartX = 0, touchStartY = 0;
        int windowStartX = 0, windowStartY = 0;
        bool movedPastTapThreshold = false;
        float tapMoveThresholdPx = 12 * density;
        _overlayContainer.Touch += (s, e) =>
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
                    _windowManager.UpdateViewLayout(_overlayContainer, _layoutParams);
                    e.Handled = true;
                    break;
                case MotionEventActions.Up:
                    if (!movedPastTapThreshold) ShareCurrentLyricLine();
                    e.Handled = true;
                    break;
            }
        };

        _windowManager.AddView(_overlayContainer, _layoutParams);

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

        // 皮肤/同步偏移/卡拉OK/双语都是在 MainPage 的设置页改的，悬浮窗这边是另一个进程/组件——不会
        // 自动知道设置变了，靠 SharedPreferences 自带的变更通知订阅一下，改了哪一项，悬浮窗（哪怕已经
        // 开着）都立刻跟着生效，不用先隐藏再重新显示一次才生效
        _settingsPrefListener = new SettingsPrefListener(() => _mainHandler?.Post(RefreshSettingsFromStore));
        MobileSettingsStore.RegisterChangeListener(_settingsPrefListener);
    }

    // ── 精简皮肤：配色 + 边框呼吸动效 ─────────────────────────────────────────────

    /// <summary>把选中的皮肤配色 + 图标套到悬浮窗上——背景/边框颜色来自 MobileSkinPalette，边框宽度/
    /// 圆角只在第一次算一遍（用的是 dp -> px 换算，屏幕密度中途不会变），换皮肤只是换颜色，不用重新
    /// 创建 Drawable 对象。图标来自 MobileSkinIconCatalog（内置皮肤，目前都是单帧）或者客制化主题
    /// 自己的 icon.rows/icon.frames——客制化主题给了 Frames 就用它（1 帧当静态图，>1 帧顺手把
    /// _iconFrameBitmaps 填上，IconFrameTick 会接着播放，见该方法注释），两者都没有才把 ImageView
    /// 隐藏掉退回纯文字，不会留一块空白占位。</summary>
    private void ApplySkin(string skinId)
    {
        PixelLyric8BitFix.MobileSkinIcon? icon;
        Bitmap[]? animatedFrames = null; // 只有客制化主题的 icon.frames 会用到，内置皮肤/单帧图标保持 null
        double frameDurationSeconds = 0;

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

                // 有 Frames 就只认 Frames、忽略 Rows（跟桌面版 CustomThemeIcon.Frames 的注释规则一样）；
                // 1 帧当静态图标处理，>1 帧才真的要跑动画。没有 Frames 才退回旧的 Rows 判断
                if (themeIcon?.Frames is { Count: > 0 } frames)
                {
                    var palette = PixelLyric8BitFix.CustomThemeValidator.BuildIconPalette(themeIcon);
                    var frameRows = frames.Select(f => f.ToArray()).ToList();
                    icon = new PixelLyric8BitFix.MobileSkinIcon(frameRows[0], palette); // 立刻显示第一帧，不用等第一次 tick
                    if (frameRows.Count > 1)
                    {
                        animatedFrames = MobilePixelIconRenderer.RenderFrames(frameRows, palette);
                        frameDurationSeconds = PixelLyric8BitFix.CustomThemeValidator.GetFrameDurationSeconds(themeIcon);
                    }
                }
                else
                {
                    icon = themeIcon?.Rows is { Count: > 0 } rows
                        ? new PixelLyric8BitFix.MobileSkinIcon(rows.ToArray(), PixelLyric8BitFix.CustomThemeValidator.BuildIconPalette(themeIcon))
                        : null;
                }
            }
            else
            {
                _currentPalette = PixelLyric8BitFix.MobileSkinCatalog.Find(null);
                icon = null;
            }
        }
        else
        {
            _currentPalette = PixelLyric8BitFix.MobileSkinCatalog.Find(skinId);
            icon = PixelLyric8BitFix.MobileSkinIconCatalog.Find(skinId);
        }

        // 换皮肤/主题了，不管新皮肤有没有动画，先把上一套的帧动画状态清掉——不清的话切到一个没有
        // 动画的皮肤，IconFrameTick 还会拿着上一套主题的 Bitmap 数组继续切，图标对不上当前选中的皮肤
        _iconFrameBitmaps = animatedFrames;
        _iconFrameDurationSeconds = frameDurationSeconds;
        _iconFrameIndex = 0;
        _iconFrameElapsedMs = 0;

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

        if (_iconView != null)
        {
            if (icon != null)
            {
                _iconView.SetImageBitmap(MobilePixelIconRenderer.Render(icon.Value));
                _iconView.Visibility = ViewStates.Visible;
            }
            else
            {
                _iconView.Visibility = ViewStates.Gone;
            }
        }
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

    /// <summary>客制化主题 icon.frames 的逐帧播放——跟桌面版 MainWindow.Skins.cs 的
    /// UpdateCustomIconFrameAnimation 是同一个"tick 累计时间、攒够一帧的时长才切"思路，只是这边按
    /// 真实毫秒数累加（IconFrameTickIntervalMs 是固定的 tick 间隔），桌面版按 50ms 一次的固定 tick
    /// 数硬算，两种写法效果一样，这边这样写更顺手。_iconFrameBitmaps 为 null 或者只有 1 张（静态图标）
    /// 就什么都不做，但这个 tick 本身照样一直跑着（跟 _pulseAction 一样常驻），不然下次 ApplySkin
    /// 切到一个真的有动画的主题时，还得再额外想办法把这个 tick 重新启动一遍。</summary>
    private void IconFrameTick()
    {
        var frames = _iconFrameBitmaps;
        if (frames is { Length: > 1 } && _iconView != null)
        {
            _iconFrameElapsedMs += IconFrameTickIntervalMs;
            double frameDurationMs = Math.Max(50, _iconFrameDurationSeconds * 1000); // 下限保护，免得 JSON 填了个离谱小的值导致每 tick 都切帧
            if (_iconFrameElapsedMs >= frameDurationMs)
            {
                _iconFrameElapsedMs = 0;
                _iconFrameIndex = (_iconFrameIndex + 1) % frames.Length;
                _iconView.SetImageBitmap(frames[_iconFrameIndex]);
            }
        }

        _mainHandler?.PostDelayed(_iconFrameAction!, IconFrameTickIntervalMs);
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

    private void BindMediaSessionManager()
    {
        _sessionManager = (AndroidMediaSession.MediaSessionManager)GetSystemService(MediaSessionService)!;
        var component = new ComponentName(this, Java.Lang.Class.FromType(typeof(MediaNotificationListenerService)));

        _activeSessionsListener = new ActiveSessionsListener(RebindController);
        _sessionManager.AddOnActiveSessionsChangedListener(_activeSessionsListener, component);

        // listener 只会在"以后列表变了"的时候推——这里先手动查一次现在已经在播的，不然要等下一次
        // 切歌/换 App 才会第一次显示出东西
        RebindController(MediaNotificationListenerService.Instance?.GetActiveMediaControllers());
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

        UpdateOverlayText();
        UpdateListeningStats(); // 复用这个 tick 顺手攒听歌时长，不用再单独开一个定时器
        _mainHandler?.PostDelayed(_renderAction!, RenderIntervalMs);
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

            string? uriString = MediaStore.Images.Media.InsertImage(ContentResolver, card, "ZipPlay歌词分享", "ZipPlay 歌词分享卡片");
            if (uriString == null)
            {
                Toast.MakeText(this, "生成分享图失败，稍后再试试", ToastLength.Short)?.Show();
                return;
            }

            var uri = global::Android.Net.Uri.Parse(uriString);
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
        _iconFrameBitmaps = null;

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

        if (_controller != null && _controllerCallback != null)
        {
            _controller.UnregisterCallback(_controllerCallback);
        }
        _controller = null;
        _controllerCallback = null;

        _mainHandler = null;

        if (_overlayContainer != null && _windowManager != null)
        {
            _windowManager.RemoveView(_overlayContainer);
        }
        _overlayContainer = null;
        _iconView = null;
        _overlayView = null;
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
