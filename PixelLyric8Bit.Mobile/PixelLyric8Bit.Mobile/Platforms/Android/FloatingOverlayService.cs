using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
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
/// </summary>
// Android 14 (API 34) 起，前台服务必须声明一个 foregroundServiceType，不声明的话 StartForeground
// 直接抛 MissingForegroundServiceTypeException 崩掉（真机上踩过——targetSdk 36 的这台设备直接崩，
// 不是警告）。这个服务干的事（悬浮窗）不属于媒体播放/定位/相机这些标准分类，只能用 SpecialUse，
// 配合 AndroidManifest.xml 里的 PROPERTY_SPECIAL_USE_FGS_SUBTYPE 说明"这个特殊用途具体是干嘛的"。
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeSpecialUse)]
public class FloatingOverlayService : Service
{
    private const string ChannelId = "zipplay_overlay";
    private const int NotificationId = 1001;

    private IWindowManager? _windowManager;
    private TextView? _overlayView;
    private WindowManagerLayoutParams? _layoutParams;

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
            .SetContentText("悬浮窗正在显示（骨架阶段，还没接真实歌词）")
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
            Text = "ZipPlay 悬浮歌词骨架\n（长按拖动试试，还没接真实歌词）",
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
    }

    private void RemoveOverlay()
    {
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
