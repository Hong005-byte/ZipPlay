using Android.App;
using Android.Content.PM;
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
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        global::AndroidX.Core.SplashScreen.SplashScreen.InstallSplashScreen(this);

        base.OnCreate(savedInstanceState);
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
