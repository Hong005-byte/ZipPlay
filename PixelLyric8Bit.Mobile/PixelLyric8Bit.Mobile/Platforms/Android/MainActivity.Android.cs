using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Media.Projection;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace PixelLyric8Bit.Mobile.Droid;

[Activity(
    MainLauncher = true,
    ConfigurationChanges = global::Uno.UI.ActivityHelper.AllConfigChanges,
    WindowSoftInputMode = SoftInput.AdjustNothing | SoftInput.StateHidden
)]
public class MainActivity : Microsoft.UI.Xaml.ApplicationActivity
{
    // 给 FullScreenPlayerPage 用的——那个页面要自己切系统状态栏/导航栏的沉浸式全屏（Window.DecorView.
    // SystemUiVisibility），这两样都得挂在 Activity 的 Window 上改，Page 本身拿不到 Activity 引用，
    // 只能反过来让 Activity 存一份自己的静态引用。App 生命周期里 MainActivity 只会创建这一份实例
    // （单 Activity 的 Uno 应用），不用在 OnDestroy 里清空。基类（BaseActivity）本来就有一个同名的
    // Current，但那边给的类型不是 MainActivity，拿不到 .Window 之外这边真正要用的东西——用 new
    // 显式盖掉，不是漏了没注意到那个警告。
    public static new MainActivity? Current { get; private set; }

    // 皮肤音乐律动要用的 MediaProjection 同意框请求码，见 RequestAudioCaptureConsent/OnActivityResult
    private const int AudioCaptureConsentRequestCode = 7001;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        global::AndroidX.Core.SplashScreen.SplashScreen.InstallSplashScreen(this);
        Current = this;

        base.OnCreate(savedInstanceState);
    }

    /// <summary>跳一次系统"开始录制"同意框，拿到手才能建 AudioPlaybackCaptureConfiguration——见
    /// AudioReactiveCapture.cs 顶部注释这整条链路为什么绕不开这一步、为什么长得很像"开始录屏"。
    /// 同意结果异步回来，见 OnActivityResult；低于 API 29（这套 API 本身要求的最低版本）直接不发起
    /// 请求，皮肤律动退回固定节奏，不报错。</summary>
    public void RequestAudioCaptureConsent()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Q) return;

        if (GetSystemService(MediaProjectionService) is not MediaProjectionManager manager) return;

        try
        {
            StartActivityForResult(manager.CreateScreenCaptureIntent(), AudioCaptureConsentRequestCode);
        }
        catch
        {
            // 拿不到这个 Intent（理论上不该发生）就静默放弃，不影响 App 其它功能
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        Android.Util.Log.Debug("ZipPlayAudio", $"OnActivityResult requestCode={requestCode} resultCode={resultCode} data={(data == null ? "null" : "present")}");
        if (requestCode != AudioCaptureConsentRequestCode) return;

        if (resultCode == Result.Ok && data != null && GetSystemService(MediaProjectionService) is MediaProjectionManager manager)
        {
            AudioReactiveCapture.OnConsentGranted(manager, resultCode, data);
        }
        else
        {
            AudioReactiveCapture.OnConsentDenied();
        }
    }

    // 硬件/手势返回键默认走 Activity 自己的返回栈，不知道 App 里还有一层 Frame 导航——不接上的话
    // 在子页面（权限/皮肤/歌词设置/统计/自定义主题）按返回键会直接把整个 Activity 关掉，翻到系统里
    // 上一个界面，不是回到 App 首页，见 App.xaml.cs 的 RootFrame 注释。子页面顶部的"‹ 返回"按钮走的
    // 是 Frame.GoBack()，这里只是让硬件返回键也能做同一件事，两条路殊途同归。
    public override void OnBackPressed()
    {
        if (PixelLyric8Bit.Mobile.App.RootFrame is { CanGoBack: true } frame)
        {
            frame.GoBack();
            return;
        }

        base.OnBackPressed();
    }
}
