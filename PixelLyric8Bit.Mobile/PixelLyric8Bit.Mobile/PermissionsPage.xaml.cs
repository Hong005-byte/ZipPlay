namespace PixelLyric8Bit.Mobile;

/// <summary>
/// 两块 Android 专属能力的验证区（对应的实现在 Platforms/Android/ 下，`#if __ANDROID__` 隔开，
/// 非 Android 平台编译时这些代码直接被预处理器去掉）：
/// - 系统媒体会话读取（MediaNotificationListenerService）——桌面版 SMTC 的对应物，读"现在系统里
///   随便哪个 App 正在播放什么"，需要用户去系统设置手动开一次"通知使用权"。
/// - 真正的悬浮窗（FloatingOverlayService）——一个前台 Service 用 IWindowManager 加一个能拖动的
///   原生 View，不是 Uno 渲染的页面；需要用户手动开一次"显示在其他应用上层"权限。
///
/// 这两块的"读到的真实播放信息接进悬浮窗里显示"这一步已经在 FloatingOverlayService 里做完了——
/// 订阅媒体会话的播放状态/元数据变化事件、联网抓真实歌词、按 PlaybackPositionEstimator 插值出的
/// 播放位置显示对应那一行，见该文件顶部注释。这个页面本身显示的还是"开没开、能不能用"这两句话，
/// 跟悬浮窗是两个独立的 UI。
///
/// 这两个权限都是跳系统设置页申请的，用户设置完手动切回 App 之后（不是通过 Frame 导航切回来，是
/// Android 任务切换直接回到还在后台的这个 Activity）不会有任何 Frame 导航事件通知这个页面"该刷新
/// 了"，所以用定时轮询顶一下——真的踩过这个坑（悬浮窗权限开了，界面死活不更新，adb appops 查底层
/// 记录才发现权限其实早就是 allow 了，是界面没刷新）。NavigationCacheMode=Required 保证这个页面
/// 只会被创建一次、定时器只会有一份在跑，不会每次 Frame.Navigate 过来就多起一个定时器。
/// </summary>
public sealed partial class PermissionsPage : Page
{
    private const int RefreshIntervalMs = 500;
    private readonly DispatcherTimer _refreshTimer = new();

    public PermissionsPage()
    {
        this.InitializeComponent();
        this.NavigationCacheMode = NavigationCacheMode.Required;

        RefreshMediaSessionStatus();
        RefreshOverlayStatus();

        _refreshTimer.Interval = TimeSpan.FromMilliseconds(RefreshIntervalMs);
        _refreshTimer.Tick += (_, _) =>
        {
            RefreshMediaSessionStatus();
            RefreshOverlayStatus();
        };
        _refreshTimer.Start();
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    // ── 系统媒体会话（通知使用权） ────────────────────────────────────────

    private void RefreshMediaSessionStatus()
    {
#if __ANDROID__
        var context = global::Android.App.Application.Context;
        if (!Droid.MediaNotificationListenerService.IsListenerAccessGranted(context))
        {
            TxtMediaSessionStatus.Text = "通知使用权：未开启（没有这个权限读不到系统正在播放什么）";
            BtnOpenNotificationSettings.Visibility = Visibility.Visible;
            return;
        }

        BtnOpenNotificationSettings.Visibility = Visibility.Collapsed;
        var service = Droid.MediaNotificationListenerService.Instance;
        if (service == null)
        {
            TxtMediaSessionStatus.Text = "通知使用权：已开启（服务还没连接上，稍等一下或者重开一次 App）";
            return;
        }

        var controllers = service.GetActiveMediaControllers();
        if (controllers.Count == 0)
        {
            TxtMediaSessionStatus.Text = "通知使用权：已开启，但现在没有检测到正在播放的 App";
            return;
        }

        var metadata = controllers[0].Metadata;
        string title = metadata?.GetString(global::Android.Media.MediaMetadata.MetadataKeyTitle) ?? "（无标题）";
        string artist = metadata?.GetString(global::Android.Media.MediaMetadata.MetadataKeyArtist) ?? "";
        TxtMediaSessionStatus.Text = "通知使用权：已开启，正在播放：" + title + (string.IsNullOrEmpty(artist) ? "" : $" - {artist}");
#else
        TxtMediaSessionStatus.Text = "通知使用权：这个功能只在 Android 上有意义";
        BtnOpenNotificationSettings.Visibility = Visibility.Collapsed;
#endif
    }

    private void BtnOpenNotificationSettings_Click(object sender, RoutedEventArgs e)
    {
#if __ANDROID__
        Droid.MediaNotificationListenerService.OpenListenerAccessSettings(global::Android.App.Application.Context);
#endif
    }

    // ── 悬浮窗权限 ─────────────────────────────────────────────────────

    private void RefreshOverlayStatus()
    {
#if __ANDROID__
        var context = global::Android.App.Application.Context;
        bool granted = Droid.FloatingOverlayService.CanDrawOverlays(context);
        TxtOverlayStatus.Text = granted
            ? (Droid.FloatingOverlayService.IsRunning ? "悬浮窗权限：已开启，悬浮窗正在显示" : "悬浮窗权限：已开启")
            : "悬浮窗权限：未开启（没有这个权限没法在别的 App 上层显示歌词）";
        BtnOpenOverlaySettings.Visibility = granted ? Visibility.Collapsed : Visibility.Visible;
        BtnToggleOverlay.IsEnabled = granted;
        BtnToggleOverlay.Content = Droid.FloatingOverlayService.IsRunning ? "隐藏悬浮窗" : "显示悬浮窗";
#else
        TxtOverlayStatus.Text = "悬浮窗权限：这个功能只在 Android 上有意义";
        BtnOpenOverlaySettings.Visibility = Visibility.Collapsed;
        BtnToggleOverlay.IsEnabled = false;
#endif
    }

    private void BtnOpenOverlaySettings_Click(object sender, RoutedEventArgs e)
    {
#if __ANDROID__
        Droid.FloatingOverlayService.OpenOverlaySettings(global::Android.App.Application.Context);
#endif
    }

    private void BtnToggleOverlay_Click(object sender, RoutedEventArgs e)
    {
#if __ANDROID__
        var context = global::Android.App.Application.Context;
        if (Droid.FloatingOverlayService.IsRunning)
        {
            Droid.FloatingOverlayService.Stop(context);
        }
        else
        {
            Droid.FloatingOverlayService.Start(context);
        }
        RefreshOverlayStatus();
#endif
    }
}
