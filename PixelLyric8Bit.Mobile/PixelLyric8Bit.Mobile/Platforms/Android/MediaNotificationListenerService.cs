using Android.App;
using Android.Content;
using Android.Media.Session;
using Android.Provider;
using Android.Service.Notification;
using AndroidX.Core.App;

namespace PixelLyric8Bit.Mobile.Droid;

/// <summary>
/// 读"系统里正在活跃的媒体会话"（Spotify、YouTube、网易云……不管谁在放）用的——这是桌面版 SMTC
/// 在 Android 上的对应物，但拿到手的方式完全不一样：Android 没有"任何 App 都能查"的通用媒体接口，
/// 得先注册一个 NotificationListenerService（因为历史原因，"读取媒体会话"这个能力挂在"通知使用权"
/// 这个系统权限底下），用户要去系统设置里手动开一次，不是装完就能用。
///
/// 只做"读"，不碰通知内容本身——这个类完全不 override OnNotificationPosted，只用
/// NotificationListenerService 这个身份去换 MediaSessionManager.GetActiveSessions 的访问权。
/// </summary>
[Service(
    Permission = "android.permission.BIND_NOTIFICATION_LISTENER_SERVICE",
    Exported = false,
    Label = "ZipPlay 歌词读取服务")]
[IntentFilter(new[] { "android.service.notification.NotificationListenerService" })]
public class MediaNotificationListenerService : NotificationListenerService
{
    // 静态持有最近一次连接上的实例，给页面直接查询当前"活跃的媒体会话"用——先用最简单的方式
    // 验证这条链路能不能跑通，不是最终架构（真正做的话应该用事件/消息通知页面刷新，而不是页面自己
    // 定时来问，这个骨架阶段先图简单）。系统觉得没必要保留这个 Service 的时候会调用
    // OnListenerDisconnected，这时候要清空，不然页面会拿着一个已经失效的实例瞎查。
    public static MediaNotificationListenerService? Instance { get; private set; }

    public override void OnListenerConnected()
    {
        base.OnListenerConnected();
        Instance = this;
    }

    public override void OnListenerDisconnected()
    {
        Instance = null;
        base.OnListenerDisconnected();
    }

    /// <summary>现在系统里所有"活跃"的媒体会话（可能有好几个 App 同时挂着，比如一边开着 Spotify
    /// 一边刷到了会自动播放视频的网页）——调用方自己决定挑哪一个，这里不做优先级判断。</summary>
    public IList<MediaController> GetActiveMediaControllers()
    {
        var manager = (MediaSessionManager)GetSystemService(MediaSessionService)!;
        var component = new ComponentName(this, Java.Lang.Class.FromType(typeof(MediaNotificationListenerService)));
        return manager.GetActiveSessions(component) ?? new List<MediaController>();
    }

    /// <summary>用户是否已经在系统设置里给过这个 App"通知使用权"——这是 GetActiveMediaControllers
    /// 能不能调用成功的前提，没给的话上面那个方法会直接抛 SecurityException。</summary>
    public static bool IsListenerAccessGranted(Context context)
    {
        var enabledPackages = NotificationManagerCompat.GetEnabledListenerPackages(context);
        return enabledPackages.Contains(context.PackageName);
    }

    /// <summary>跳去系统设置的"通知使用权"页面——这是个特殊权限，没法用普通的运行时权限弹窗申请，
    /// 必须让用户自己在设置列表里找到这个 App 手动打开，跟悬浮窗权限是同一个套路（见
    /// FloatingOverlayService 里申请悬浮窗权限那部分）。</summary>
    public static void OpenListenerAccessSettings(Context context)
    {
        var intent = new Intent(Settings.ActionNotificationListenerSettings);
        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }
}
