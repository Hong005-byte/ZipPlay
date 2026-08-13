using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 启动设置页：不是账号登录，只是让用户在打开主播放器窗口之前
    /// 选好皮肤 / 尺寸 / 显示模式，选择结果落地为本地配置文件。
    /// </summary>
    public partial class SettingsWindow : Window
    {
        // App 用的是 ShutdownMode="OnExplicitShutdown"，所以这个窗口无论怎么被关掉
        // （点"开始播放"、或者直接按标题栏的 X）都要自己决定：是继续流程，还是退出整个 App。
        private bool _proceeding = false;

        private readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        private string? _foundUpdateUrl;

        public SettingsWindow()
        {
            InitializeComponent();

            Closed += (s, e) =>
            {
                if (!_proceeding) Application.Current.Shutdown();
            };

            var settings = AppSettings.Load();

            foreach (var child in SkinPanel.Children)
            {
                if (child is RadioButton rb && rb.Tag is string tag && Enum.TryParse<PlayerSkin>(tag, out var skin))
                {
                    rb.IsChecked = skin == settings.Skin;
                }
            }

            RbSizeSmall.IsChecked = settings.Size == PlayerSize.Small;
            RbSizeMedium.IsChecked = settings.Size == PlayerSize.Medium;
            RbSizeLarge.IsChecked = settings.Size == PlayerSize.Large;

            RbModeStandard.IsChecked = settings.DisplayMode == PlayerDisplayMode.Standard;
            RbModeMinimal.IsChecked = settings.DisplayMode == PlayerDisplayMode.Minimal;

            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            TxtCurrentVersion.Text = $"当前版本 v{currentVersion.ToString(3)}";
        }

        private PlayerSkin GetSelectedSkin()
        {
            foreach (var child in SkinPanel.Children)
            {
                if (child is RadioButton { IsChecked: true } rb && rb.Tag is string tag &&
                    Enum.TryParse<PlayerSkin>(tag, out var skin))
                {
                    return skin;
                }
            }
            return PlayerSkin.Minecraft;
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            var settings = new AppSettings
            {
                Skin = GetSelectedSkin(),
                Size = RbSizeSmall.IsChecked == true ? PlayerSize.Small
                     : RbSizeLarge.IsChecked == true ? PlayerSize.Large
                     : PlayerSize.Medium,
                DisplayMode = RbModeMinimal.IsChecked == true ? PlayerDisplayMode.Minimal : PlayerDisplayMode.Standard,
            };
            settings.Save();

            _proceeding = true;
            var main = new MainWindow(settings);
            Application.Current.MainWindow = main;
            main.Show();

            Close();
        }

        // 手动检查更新：不用等下次启动主窗口的后台检查，点一下马上就知道结果
        private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckUpdate.IsEnabled = false;
            TxtUpdateStatus.Visibility = Visibility.Visible;
            TxtUpdateStatus.Cursor = Cursors.Arrow;
            TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
            TxtUpdateStatus.Text = "🔄 检查中...";
            _foundUpdateUrl = null;

            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            var result = await UpdateChecker.CheckAsync(currentVersion, _httpClient);

            if (!result.Success)
            {
                TxtUpdateStatus.Text = "⚠️ 检查失败，请确认网络连接后重试";
                TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x88, 0x88));
            }
            else if (result.Update != null)
            {
                _foundUpdateUrl = result.Update.ReleaseUrl;
                TxtUpdateStatus.Text = $"🎉 发现新版本 v{result.Update.Version}，点击这里前往下载";
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

        private void TxtUpdateStatus_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (string.IsNullOrEmpty(_foundUpdateUrl)) return;
            try
            {
                Process.Start(new ProcessStartInfo(_foundUpdateUrl) { UseShellExecute = true });
            }
            catch { /* 打不开浏览器也不该崩程序 */ }
        }
    }
}
