using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PixelLyric8BitFix
{
    /// <summary>版本号 + 检查更新（含一键下载装） + 几条使用小贴士——从 HomeWindow 的"ℹ️ 关于与更新"格子进来。</summary>
    public partial class AboutWindow : Window
    {
        private readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };

        // 查版本（小 JSON 请求）跟下安装包（几十 MB 的文件）不能共用同一个短超时的 HttpClient——
        // HttpClient.Timeout 管的是整个请求（包括读响应体），4~6 秒对一个大文件下载来说太容易半路被打断。
        // 详见 MainWindow.xaml.cs 里同样的两个 HttpClient 的注释。
        private readonly HttpClient _downloadHttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        private UpdateInfo? _foundUpdate;
        private bool _updateInProgress;

        public AboutWindow()
        {
            InitializeComponent();

            // GitHub API 强制要求请求带 User-Agent，不带会直接 403——之前漏配过这个，
            // 403 被 UpdateChecker 当成"没有更新"处理，导致明明有新版本却显示"已是最新版本"
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ZipPlay-UpdateChecker");
            _downloadHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ZipPlay-UpdateChecker");

            Closed += (s, e) => _downloadHttpClient.Dispose();

            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            TxtCurrentVersion.Text = $"当前版本 v{currentVersion.ToString(3)}";
        }

        // 手动检查更新：不用等下次启动主窗口的后台检查，点一下马上就知道结果
        private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckUpdate.IsEnabled = false;
            TxtUpdateStatus.Visibility = Visibility.Visible;
            TxtUpdateStatus.Cursor = Cursors.Arrow;
            TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
            TxtUpdateStatus.Text = "🔄 检查中...";
            _foundUpdate = null;

            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            var result = await UpdateChecker.CheckAsync(currentVersion, _httpClient);

            if (!result.Success)
            {
                TxtUpdateStatus.Text = "⚠️ 检查失败，请确认网络连接后重试";
                TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x88, 0x88));
            }
            else if (result.Update != null)
            {
                _foundUpdate = result.Update;
                bool canOneClick = !string.IsNullOrEmpty(result.Update.InstallerDownloadUrl);
                TxtUpdateStatus.Text = canOneClick
                    ? $"🎉 发现新版本 v{result.Update.Version}，点击这里立即更新"
                    : $"🎉 发现新版本 v{result.Update.Version}，点击这里前往下载";
                TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0xFF, 0x55));
                TxtUpdateStatus.Cursor = Cursors.Hand;
            }
            else
            {
                TxtUpdateStatus.Text = "✅ 已经是最新版本";
                TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
            }

            BtnCheckUpdate.IsEnabled = true;
        }

        // 有安装包直链就一键下载 + 静默装 + 自动重启进新版本；没有直链就退回打开浏览器
        private async void TxtUpdateStatus_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_updateInProgress || _foundUpdate == null) return;

            if (string.IsNullOrEmpty(_foundUpdate.InstallerDownloadUrl))
            {
                try { Process.Start(new ProcessStartInfo(_foundUpdate.ReleaseUrl) { UseShellExecute = true }); } catch { }
                return;
            }

            _updateInProgress = true;
            TxtUpdateStatus.Cursor = Cursors.Arrow;
            TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
            TxtUpdateStatus.Text = "⬇ 下载中... 0%";

            try
            {
                var progress = new Progress<double>(p =>
                {
                    TxtUpdateStatus.Text = $"⬇ 下载中... {(int)(p * 100)}%";
                });

                string installerPath = await UpdateChecker.DownloadInstallerAsync(
                    _foundUpdate.InstallerDownloadUrl, _downloadHttpClient, progress, CancellationToken.None);

                TxtUpdateStatus.Text = "✅ 正在安装...";
                UpdateChecker.LaunchInstallerAndExit(installerPath); // 这个方法自己会调 Application.Current.Shutdown()，不用这边额外处理关闭逻辑
            }
            catch (Exception ex)
            {
                _updateInProgress = false;
                TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x88, 0x88));
                TxtUpdateStatus.Text = "⚠️ 下载失败，点击重试";
                TxtUpdateStatus.Cursor = Cursors.Hand;
                AppLog.Error("AboutWindow update download/install", ex);
            }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e) => Close();
    }
}
