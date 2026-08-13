using System;
using System.Windows;
using System.Windows.Controls;

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
    }
}
