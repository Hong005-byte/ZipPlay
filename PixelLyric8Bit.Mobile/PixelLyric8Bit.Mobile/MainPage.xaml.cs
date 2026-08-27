using System.Diagnostics;

namespace PixelLyric8Bit.Mobile;

/// <summary>
/// 悬浮歌词界面骨架——第一步做的是"歌词按时间戳切换"这条链路（见 Timer_Tick），用的是
/// PixelLyric8Bit.Core 里跟桌面版同一份 <see cref="PixelLyric8BitFix.LrcParser"/>。
///
/// 这一步在第一版基础上加了两块 Android 专属能力的验证面板（对应的实现在 Platforms/Android/
/// 下，`#if __ANDROID__` 隔开，非 Android 平台编译时这些代码直接被预处理器去掉）：
/// - 系统媒体会话读取（MediaNotificationListenerService）——桌面版 SMTC 的对应物，读"现在系统里
///   随便哪个 App 正在播放什么"，需要用户去系统设置手动开一次"通知使用权"。
/// - 真正的悬浮窗（FloatingOverlayService）——一个前台 Service 用 IWindowManager 加一个能拖动的
///   原生 View，不是 Uno 渲染的页面；需要用户手动开一次"显示在其他应用上层"权限。
///
/// 两块目前还是各自独立验证（状态区显示各自的信息），还没有把"读到的真实播放信息"接进悬浮窗
/// 里显示——那是下一步：两条链路都验证通了，才有把它们接在一起的意义。
/// </summary>
public sealed partial class MainPage : Page
{
    private readonly List<(int TimeMs, string Text)> _lines;
    private readonly DispatcherTimer _timer = new();
    private readonly Stopwatch _stopwatch = new();
    private readonly int _loopDurationMs;

    public MainPage()
    {
        this.InitializeComponent();

        // 骨架阶段先用一份写死的示例 LRC 循环播放；真正的歌词来源接上系统媒体会话之后再替换这一段
        const string sampleLrc = """
            [00:00.00]ZipPlay Mobile 悬浮歌词骨架
            [00:03.00]这行字是 PixelLyric8Bit.Core 的 LrcParser 解析出来的
            [00:07.00]跟桌面版用的是同一份解析逻辑，不是重新写了一遍
            [00:11.00]下一步要接真实的系统媒体会话和悬浮窗权限
            [00:15.50]这一句播完会自动循环回到第一句
            """;
        _lines = PixelLyric8BitFix.LrcParser.ParseLines(sampleLrc);
        _loopDurationMs = (_lines.Count > 0 ? _lines[^1].TimeMs : 0) + 4000; // 最后一句放完留 4 秒再循环，不是切完立刻跳回去

        TxtDynamicLyric.Text = _lines.Count > 0 ? _lines[0].Text : "（示例歌词解析失败）";

        _timer.Interval = TimeSpan.FromMilliseconds(100);
        _timer.Tick += Timer_Tick;
        _stopwatch.Start();
        _timer.Start();

        RefreshMediaSessionStatus();
        RefreshOverlayStatus();
    }

    private void Timer_Tick(object? sender, object e)
    {
        if (_loopDurationMs <= 0) return;
        int elapsedInLoop = (int)(_stopwatch.ElapsedMilliseconds % _loopDurationMs);

        // 找"时间戳不超过当前播放位置的最后一行"——跟桌面版歌词同步走的是同一个思路
        // （MainWindow.Lyrics.cs 里也是这么找当前该显示哪一行的），只是这里数据源是内存里的示例数据，
        // 不是真实播放位置
        string? current = null;
        foreach (var (timeMs, text) in _lines)
        {
            if (timeMs > elapsedInLoop) break;
            current = text;
        }
        TxtDynamicLyric.Text = current ?? "...";

        // 顺手每 tick 刷一下两个权限的状态——骨架阶段图省事，用定时轮询代替事件通知，真正做的话
        // 应该是 MediaController.Callback / Activity 生命周期事件主动推变化过来，不是页面自己每
        // 100ms 问一次。这两个权限都是跳系统设置页申请的，用户设置完手动切回 App 之后 MainPage
        // 这个实例并不会被重新创建（只是从后台切回前台），不放这里刷的话状态会一直停在"跳转去设置
        // 那一刻"，哪怕权限其实已经开了，界面还是显示"未开启"——真的踩过这个坑（悬浮窗权限开了，
        // 界面死活不更新，adb appops 查底层记录才发现权限其实早就是 allow 了，是界面没刷新）。
        RefreshMediaSessionStatus();
        RefreshOverlayStatus();
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
