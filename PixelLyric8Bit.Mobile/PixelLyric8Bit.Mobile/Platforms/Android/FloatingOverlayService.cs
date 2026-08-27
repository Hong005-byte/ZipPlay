using System.Net.Http;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Media;
using Android.OS;
using Android.Provider;
using Android.Runtime;
using Android.Views;
using Android.Widget;

namespace PixelLyric8Bit.Mobile.Droid;

/// <summary>
/// 真正的"悬浮窗"——不是 Uno 渲染的一个页面/窗口，是一个前台 Service 自己用 IWindowManager 往系统
/// 窗口层加一个原生 Android View（这里先用一个朴素的 TextView，还没接 Uno/Skia 渲染管线；Uno 的
/// 渲染整套是绑在 MainActivity 那个 Activity 窗口上的，"脱离 Activity、飘在所有 App 上面"这件事本身
/// 跟 Uno 的单项目模型没关系，是 Android 系统层面"这块窗口该由谁来管"的问题，所以先用最朴素的原生
/// View 验证"悬浮窗权限 -> 加一个能拖动的浮窗"这条链路走不走得通，视觉样式（像素风）以后再考虑要不要
/// 想办法接进来）。
///
/// 悬浮窗权限（SYSTEM_ALERT_WINDOW）是特殊权限，跟"通知使用权"（MediaNotificationListenerService）
/// 同一个套路：普通的运行时权限弹窗申请不了，必须让用户自己去系统设置里手动开一次。
///
/// 这一版把两条链路真的接在一起了：每秒查一次 MediaNotificationListenerService 读到的"现在在播什么"，
/// 歌曲变了就用 PixelLyric8Bit.Core 的 LyricsFetcher（跟桌面版同一份多引擎并发抓词逻辑）联网抓一份
/// LRC，抓到之后跟着系统报告的真实播放位置（MediaController.PlaybackState.Position）显示对应那一行——
/// 这就是桌面版"歌词按时间戳同步显示"这件事，在 Android 上第一次用真实数据跑通。
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeSpecialUse)]
public class FloatingOverlayService : Service
{
    private const string ChannelId = "zipplay_overlay";
    private const int NotificationId = 1001;
    private const int PollIntervalMs = 1000; // 骨架阶段图省事用定时轮询，真正做的话应该用 MediaController.Callback 推变化

    private IWindowManager? _windowManager;
    private TextView? _overlayView;
    private WindowManagerLayoutParams? _layoutParams;
    private Handler? _pollHandler;
    private Action? _pollAction;

    private readonly HttpClient _httpClient = new();
    private PixelLyric8BitFix.LyricsFetcher? _lyricsFetcher;
    private string? _fetchedForTrackKey; // "标题|艺人"，用来判断歌曲是不是变了，变了才重新抓词，不然每秒都联网一次
    private List<(int TimeMs, string Text)>? _currentLines;
    private bool _fetchInFlight;
    private System.Threading.CancellationTokenSource? _fetchCts;

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

        _overlayView = new TextView(this)
        {
            Text = "ZipPlay 悬浮歌词骨架\n（长按拖动试试，正在连接系统媒体会话…）",
            TextSize = 14,
        };
        _overlayView.SetTextColor(Color.ParseColor("#FFFF55"));
        _overlayView.SetBackgroundColor(Color.ParseColor("#D9111111"));
        _overlayView.SetPadding(32, 20, 32, 20);

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
        // LayoutParams 对象最后一次 UpdateViewLayout 传的位置，不用自己再存一份
        float touchStartX = 0, touchStartY = 0;
        int windowStartX = 0, windowStartY = 0;
        _overlayView.Touch += (s, e) =>
        {
            var ev = e.Event!;
            switch (ev.Action)
            {
                case MotionEventActions.Down:
                    windowStartX = _layoutParams.X;
                    windowStartY = _layoutParams.Y;
                    touchStartX = ev.RawX;
                    touchStartY = ev.RawY;
                    e.Handled = true;
                    break;
                case MotionEventActions.Move:
                    _layoutParams.X = windowStartX + (int)(ev.RawX - touchStartX);
                    _layoutParams.Y = windowStartY + (int)(ev.RawY - touchStartY);
                    _windowManager.UpdateViewLayout(_overlayView, _layoutParams);
                    e.Handled = true;
                    break;
            }
        };

        _windowManager.AddView(_overlayView, _layoutParams);

        _lyricsFetcher = new PixelLyric8BitFix.LyricsFetcher(_httpClient);
        _pollHandler = new Handler(Looper.MainLooper!);
        _pollAction = PollAndUpdate;
        _pollHandler.Post(_pollAction); // 立刻先跑一次，不用等第一个 PollIntervalMs 过去才有内容
    }

    // 每 PollIntervalMs 跑一次：查现在系统里在播什么、要不要重新抓词、该显示哪一行歌词——
    // 全部在主线程（Handler 绑的是 MainLooper）跑，直接改 TextView.Text 不用切线程，联网抓词那部分
    // 是 fire-and-forget 的异步任务，不会卡住这个轮询本身
    private void PollAndUpdate()
    {
        if (_overlayView == null) return; // 悬浮窗已经被关掉了，不用再排下一次

        UpdateOverlayText();
        _pollHandler?.PostDelayed(_pollAction!, PollIntervalMs);
    }

    private void UpdateOverlayText()
    {
        var controller = GetActiveController();
        if (controller == null)
        {
            _overlayView!.Text = "ZipPlay 悬浮歌词骨架\n（现在没有检测到正在播放的 App）";
            return;
        }

        string title = controller.Metadata?.GetString(MediaMetadata.MetadataKeyTitle) ?? "（无标题）";
        string artist = controller.Metadata?.GetString(MediaMetadata.MetadataKeyArtist) ?? "";
        long durationMs = controller.Metadata?.GetLong(MediaMetadata.MetadataKeyDuration) ?? 0;
        string trackKey = title + "|" + artist;

        if (trackKey != _fetchedForTrackKey && !_fetchInFlight)
        {
            _fetchedForTrackKey = trackKey;
            _currentLines = null;
            _fetchCts?.Cancel();
            _fetchCts = new System.Threading.CancellationTokenSource();
            _ = FetchLyricsAsync(title, artist, TimeSpan.FromMilliseconds(durationMs), _fetchCts.Token);
        }

        if (_currentLines == null)
        {
            _overlayView!.Text = _fetchInFlight
                ? $"{title} - {artist}\n（正在联网找歌词…）"
                : $"{title} - {artist}\n（没找到歌词）";
            return;
        }

        // MediaController.PlaybackState.Position 是"上次系统汇报时的位置"，不是实时值——严格来说应该
        // 跟桌面版一样按 LastPositionUpdateTime 再插值一下，这里骨架阶段图简单，每秒查一次直接用汇报值，
        // 一秒以内的漂移用肉眼基本看不出来
        long positionMs = controller.PlaybackState?.Position ?? 0;

        string? current = null;
        foreach (var (timeMs, text) in _currentLines)
        {
            if (timeMs > positionMs) break;
            current = text;
        }
        _overlayView!.Text = current ?? $"{title} - {artist}";
    }

    private async System.Threading.Tasks.Task FetchLyricsAsync(string title, string artist, TimeSpan expectedDuration, System.Threading.CancellationToken token)
    {
        _fetchInFlight = true;
        try
        {
            var result = await _lyricsFetcher!.FetchAsync(title, artist, expectedDuration, token);
            if (token.IsCancellationRequested) return; // 抓词这几秒里歌又换了，这份结果作废

            _currentLines = result != null
                ? PixelLyric8BitFix.LrcParser.ParseLines(result.Lrc)
                : new List<(int TimeMs, string Text)>(); // 空列表当"确实找不到"，跟"还没抓完"（null）区分开
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
    }

    // 特意写成全限定名——这个文件同时 using 了 Android.Widget（给 TextView 用）和这里要的
    // Android.Media.Session.MediaController，两个命名空间都有一个叫 MediaController 的类型
    // （Android.Widget.MediaController 是给视频播放器用的老 UI 控件，跟这里的媒体会话完全是两回事），
    // 裸写 MediaController 会被解析成 Android.Widget 那个，编译期就报"没有 Metadata/PlaybackState"
    private global::Android.Media.Session.MediaController? GetActiveController()
    {
        var service = MediaNotificationListenerService.Instance;
        if (service == null) return null;
        var controllers = service.GetActiveMediaControllers();
        return controllers.Count > 0 ? controllers[0] : null;
    }

    private void RemoveOverlay()
    {
        if (_pollHandler != null && _pollAction != null)
        {
            _pollHandler.RemoveCallbacks(_pollAction);
        }
        _pollHandler = null;
        _pollAction = null;

        if (_overlayView != null && _windowManager != null)
        {
            _windowManager.RemoveView(_overlayView);
        }
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
}
