using System.Windows;

namespace PixelLyric8BitFix
{
    /// <summary>窗口尺寸 / 显示模式 / 歌词字号——从 HomeWindow 的"📐 窗口与显示"格子进来，
    /// 打开读一次 AppSettings，关闭时存回去，自己管自己这三个字段。</summary>
    public partial class PlayerAppearanceWindow : Window
    {
        public PlayerAppearanceWindow()
        {
            InitializeComponent();

            var settings = AppSettings.Load();
            RbSizeSmall.IsChecked = settings.Size == PlayerSize.Small;
            RbSizeMedium.IsChecked = settings.Size == PlayerSize.Medium;
            RbSizeLarge.IsChecked = settings.Size == PlayerSize.Large;

            RbModeStandard.IsChecked = settings.DisplayMode == PlayerDisplayMode.Standard;
            RbModeMinimal.IsChecked = settings.DisplayMode == PlayerDisplayMode.Minimal;

            RbFontSmall.IsChecked = settings.FontSize == LyricFontSize.Small;
            RbFontMedium.IsChecked = settings.FontSize == LyricFontSize.Medium;
            RbFontLarge.IsChecked = settings.FontSize == LyricFontSize.Large;

            Closed += (s, e) =>
            {
                var toSave = AppSettings.Load();
                toSave.Size = RbSizeSmall.IsChecked == true ? PlayerSize.Small
                            : RbSizeLarge.IsChecked == true ? PlayerSize.Large
                            : PlayerSize.Medium;
                toSave.DisplayMode = RbModeMinimal.IsChecked == true ? PlayerDisplayMode.Minimal : PlayerDisplayMode.Standard;
                toSave.FontSize = RbFontSmall.IsChecked == true ? LyricFontSize.Small
                                 : RbFontLarge.IsChecked == true ? LyricFontSize.Large
                                 : LyricFontSize.Medium;
                toSave.Save();
            };
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e) => Close();
    }
}
