using System.Net.Http;

namespace PixelLyric8Bit.Mobile;

/// <summary>
/// 关于与更新——版本号 + 手动检查更新，对应桌面版 AboutWindow。检查逻辑全在 Core 的
/// MobileUpdateChecker（跟桌面版 UpdateChecker 同一个"靠 GitHub Release 判断"的思路，但手机版走
/// 自己的 "mobile-v" tag 前缀、查的是 /releases 列表不是 /latest，见该类顶部注释为什么两边不能共用
/// 同一条判断逻辑），这个页面只管把结果显示出来；真正下载 APK、唤出系统安装器这两步交给
/// Platforms/Android 的 AndroidUpdateInstaller——Android 没有桌面版那种"静默跑安装包、装完自动重启
/// App"的路，用户必须自己在系统安装器上点一下"安装"/"更新"确认，这是系统安全机制卡的，不是这边
/// 没做完，见该文件顶部注释。
///
/// 没有像桌面版那样在 App 启动时自动后台静默查一次——手机版目前还没发过 Release（见 README
/// 「发布新版本」那节要求先手动打 "mobile-v" 开头的 tag），启动时自动查一次大概率只会查到"还没有
/// 手机版 Release"，一直是这个没意义的结果，不如等真的发过第一个 Release 之后再考虑要不要加，
/// 现在先只留手动检查这一条路，跟 PermissionsPage 那几个"检查中…"的实时轮询不是一回事。
/// </summary>
public sealed partial class AboutPage : Page
{
    private readonly HttpClient _httpClient = new();
    private PixelLyric8BitFix.MobileUpdateInfo? _pendingUpdate;

    public AboutPage()
    {
        this.InitializeComponent();
        this.NavigationCacheMode = NavigationCacheMode.Required;

        TxtVersion.Text = $"版本 {AppVersion.Current}";
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        BtnDownloadUpdate.Visibility = Visibility.Collapsed;
        _pendingUpdate = null;
        TxtUpdateStatus.Text = "正在检查…";

        if (!Version.TryParse(AppVersion.Current, out var currentVersion))
        {
            TxtUpdateStatus.Text = "本地版本号格式不对，没法比较（这是个 bug，麻烦反馈一下）";
            return;
        }

        var result = await PixelLyric8BitFix.MobileUpdateChecker.CheckAsync(currentVersion, _httpClient);
        if (!result.Success)
        {
            TxtUpdateStatus.Text = "检查失败——可能是网络问题，稍后再试试";
            return;
        }
        if (result.Update == null)
        {
            TxtUpdateStatus.Text = "已经是最新版本";
            return;
        }

        _pendingUpdate = result.Update;
        bool hasApk = result.Update.ApkDownloadUrl != null;
        TxtUpdateStatus.Text = $"发现新版本 {result.Update.Version}" + (hasApk ? "" : "（这个 Release 里没有找到安装包直链，点下面按钮去发布页手动下载）");
        BtnDownloadUpdate.Content = hasApk ? "下载并安装更新" : "去发布页下载";
        BtnDownloadUpdate.Visibility = Visibility.Visible;
    }

    private async void BtnDownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate == null) return;

#if __ANDROID__
        if (_pendingUpdate.ApkDownloadUrl == null)
        {
            // 没有直链——引导去 Release 页面自己手动下载，用系统浏览器打开，不用系统安装器那条路
            var intent = new Android.Content.Intent(Android.Content.Intent.ActionView, Android.Net.Uri.Parse(_pendingUpdate.ReleaseUrl));
            intent.AddFlags(Android.Content.ActivityFlags.NewTask);
            global::Android.App.Application.Context.StartActivity(intent);
            return;
        }

        BtnDownloadUpdate.IsEnabled = false;
        ProgressDownload.Visibility = Visibility.Visible;
        ProgressDownload.IsIndeterminate = false;
        TxtUpdateStatus.Text = "正在下载…";

        try
        {
            var progress = new Progress<double>(p => ProgressDownload.Value = p * 100);
            string path = await Droid.AndroidUpdateInstaller.DownloadApkAsync(_pendingUpdate.ApkDownloadUrl, _httpClient, progress, CancellationToken.None);
            TxtUpdateStatus.Text = "下载完成，正在唤出安装界面……如果没弹出来，去系统设置里给这个 App 开一次「允许安装未知来源应用」再试。";
            Droid.AndroidUpdateInstaller.LaunchInstaller(path);
        }
        catch (Exception ex)
        {
            TxtUpdateStatus.Text = "下载失败：" + ex.Message;
        }
        finally
        {
            BtnDownloadUpdate.IsEnabled = true;
            ProgressDownload.Visibility = Visibility.Collapsed;
        }
#else
        TxtUpdateStatus.Text = "下载并安装更新：这个功能只在 Android 上有意义";
#endif
    }
}
