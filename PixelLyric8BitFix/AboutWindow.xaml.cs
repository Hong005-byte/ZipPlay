using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PixelLyric8BitFix
{
    /// <summary>版本号 + 检查更新（含一键下载装） + 更新日志 + 几条使用小贴士——从 HomeWindow 的
    /// "ℹ️ 关于与更新"格子进来。</summary>
    public partial class AboutWindow : Window
    {
        // 更新日志：每个版本改了什么，列出来存个档。内容是随安装包一起打包的静态数据，不是从网络拉的——
        // 离线也看得到，也不用为了一份变更记录单独起一个后端。数组按"最新在前"排列（ChangelogPanel
        // 直接按数组顺序往下画），下次发新版本时把新的一条插到最前面（Program.cs/csproj 里的 Version
        // 也要记得同步改，这里不会自动读那个版本号）——旧版本的条目留着不用删，这就是完整的更新历史。
        // 每条只写"用户能感知到的变化"，不用照抄 commit message 或者解释实现细节。
        private static readonly (string Version, string Date, string[] Highlights)[] ChangelogEntries =
        {
            ("2.5.0", "2026-09", new[]
            {
                "Mini 模式桌宠的单击反应联动客制化主题的 icon.actions：弹气泡的同时也会真的切一次姿势，不再只是弹一下",
                "图标画板新增「点击测试」：实际大小预览图现在能直接点，模拟真实播放器点装饰图标的效果，不用再插入编辑框、套进真实播放器才能验证",
                "图标画板的动作条支持拖拽调整循环顺序，不用再手动数第几个、手改 JSON 数组元素",
                "写了 icon.actions 的客制化主题，装饰图标会多一圈轻微的呼吸描边，提示这里能点——以前只能靠鼠标移上去变手型光标才知道",
            }),
        };

        private void BuildChangelogUi()
        {
            ChangelogPanel.Children.Clear();
            var mutedBrush = (Brush)FindResource("MutedTextBrush");
            var hintBrush = (Brush)FindResource("HintTextBrush");
            foreach (var entry in ChangelogEntries)
            {
                ChangelogPanel.Children.Add(new TextBlock
                {
                    Text = $"v{entry.Version} · {entry.Date}",
                    Foreground = mutedBrush,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 6, 0, 2),
                });
                foreach (var line in entry.Highlights)
                {
                    ChangelogPanel.Children.Add(new TextBlock
                    {
                        Text = $"· {line}",
                        Foreground = hintBrush,
                        FontSize = 10,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(4, 1, 0, 1),
                    });
                }
            }
        }

        // 两个都走 NetworkHelpers 建，自带强制 IPv4，详见 NetworkHelpers 类注释和 MainWindow.xaml.cs
        // 里同样的两个 HttpClient 的注释。
        private readonly HttpClient _httpClient = NetworkHelpers.CreateHttpClient(TimeSpan.FromSeconds(6));

        // 查版本（小 JSON 请求）跟下安装包（几十 MB 的文件）不能共用同一个短超时的 HttpClient——
        // HttpClient.Timeout 管的是整个请求（包括读响应体），4~6 秒对一个大文件下载来说太容易半路被打断。
        private readonly HttpClient _downloadHttpClient = NetworkHelpers.CreateHttpClient(TimeSpan.FromMinutes(10));
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

            BuildChangelogUi();
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
