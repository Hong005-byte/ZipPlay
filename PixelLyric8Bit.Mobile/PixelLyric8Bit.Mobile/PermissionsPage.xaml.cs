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

#if __ANDROID__
        // 只在页面刚建好这一次读设置初始化勾选框——之后 ChkMusicReactive_Toggled 是唯一改这个设置的
        // 地方，不能让下面 500ms 一次的定时器也去碰这个勾选框，不然用户刚点一下会被下一次 tick 立刻
        // 拽回去（定时器只用来刷新"现在真的采集到没有"那行状态文字，见 RefreshMusicReactiveStatus）
        ChkMusicReactive.IsChecked = Droid.MobileSettingsStore.MusicReactiveEnabled;
#endif

        RefreshMediaSessionStatus();
        RefreshOverlayStatus();
        RefreshMusicReactiveStatus();

        _refreshTimer.Interval = TimeSpan.FromMilliseconds(RefreshIntervalMs);
        _refreshTimer.Tick += (_, _) =>
        {
            RefreshMediaSessionStatus();
            RefreshOverlayStatus();
            RefreshMusicReactiveStatus();
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

    // ── 皮肤音乐律动 ───────────────────────────────────────────────────────

    /// <summary>只刷新"现在真的采集到没有"这行状态文字，不碰 ChkMusicReactive 本身（那是设置项，
    /// 不是可以被系统悄悄改掉的东西，见构造函数注释）。三种状态：设置没开／设置开了但这次进程还没
    /// 真的走过同意框（IsActive=false）／已经在真的采集。</summary>
    private void RefreshMusicReactiveStatus()
    {
#if __ANDROID__
        if (!Droid.MobileSettingsStore.MusicReactiveEnabled)
        {
            TxtMusicReactiveStatus.Text = "状态：没开";
            return;
        }
        TxtMusicReactiveStatus.Text = Droid.AudioReactiveCapture.IsActive
            ? "状态：已经在采集，跟着响度/鼓点变速中"
            : "状态：已经勾选，但这次还没拿到系统同意（App 完全重启过、或者刚勾上还没跳完同意框）";
#else
        TxtMusicReactiveStatus.Text = "皮肤音乐律动：这个功能只在 Android 上有意义";
#endif
    }

    /// <summary>勾上就立刻跳一次系统同意框（RequestAudioCaptureConsent 内部处理了拿不到 Activity/
    /// 系统版本太低这些情况，不用在这里判断），不需要用户再单独点一个"申请"按钮——这个开关本身就是
    /// 触发点，跟桌面版"翻一下开关就生效"是同一个体验。取消勾选就把已经在跑的采集停掉，收回
    /// MediaProjection token（不这么做的话状态栏"正在被捕获"那个提示会一直挂着，用户会疑惑"我明明
    /// 关了怎么还在录"）。</summary>
    private void ChkMusicReactive_Toggled(object sender, RoutedEventArgs e)
    {
#if __ANDROID__
        bool enabled = ChkMusicReactive.IsChecked == true;

        // 真机踩过的坑：Android 要求"拿 MediaProjection token 这一刻之前"就已经是一个带
        // TypeMediaProjection 的前台服务在跑（见 FloatingOverlayService.StartForegroundWithNotification
        // 那段注释），不是拿到 token 之后才补声明。悬浮窗没开着的话这个前台服务压根没在跑，这时候跳
        // 同意框只会换来一次必然失败的 SecurityException——所以这里先检查悬浮窗开没开，没开就提示
        // 用户先开悬浮窗，不白跳一次注定失败的系统同意框
        if (enabled && !Droid.FloatingOverlayService.IsRunning)
        {
            ChkMusicReactive.IsChecked = false; // 挡下这次勾选，不留一个"勾着但其实没生效"的假状态
            TxtMusicReactiveStatus.Text = "状态：请先点上面「显示悬浮窗」，悬浮窗开着的时候才能开这个开关";
            return;
        }

        Droid.MobileSettingsStore.MusicReactiveEnabled = enabled;

        if (enabled)
        {
            Droid.MainActivity.Current?.RequestAudioCaptureConsent();
        }
        else
        {
            Droid.AudioReactiveCapture.Stop();
        }
        RefreshMusicReactiveStatus();
#endif
    }
}
