using System.Globalization;
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
            RbSizeCustom.IsChecked = settings.Size == PlayerSize.Custom;
            TxtCustomWidth.Text = settings.GetClampedCustomWidth().ToString("0", CultureInfo.InvariantCulture);
            TxtCustomWidth.IsEnabled = settings.Size == PlayerSize.Custom; // 跟 RbSizeCustom_CheckedChanged 是同一份判断，这里是"打开页面时的初始状态"

            RbModeStandard.IsChecked = settings.DisplayMode == PlayerDisplayMode.Standard;
            RbModeMinimal.IsChecked = settings.DisplayMode == PlayerDisplayMode.Minimal;

            RbFontSmall.IsChecked = settings.FontSize == LyricFontSize.Small;
            RbFontMedium.IsChecked = settings.FontSize == LyricFontSize.Medium;
            RbFontLarge.IsChecked = settings.FontSize == LyricFontSize.Large;

            // 预览图标随便找了个通用的音符图标，不是哪套皮肤专属的，纯粹演示"缩放这件事本身长什么样"
            IconScalePreview.Source = PixelArt.CreateNoteIcon();

            // Slider.Value 夹一下范围——理论上存进配置文件的值已经是合法范围内的（GetClampedIconScale
            // 只在应用时夹，不影响存进去的原始值），但直接从文件读出来的东西还是稳妥一点，不然滑块
            // 会因为 Value 超出 Minimum/Maximum 而直接钳在两端，用户会以为滑块坏了
            SliderIconScale.Value = settings.GetClampedIconScale();
            UpdateIconScaleLabel(SliderIconScale.Value);

            // 放在这之后才订阅——见 XAML 里 SliderIconScale 那段注释：到这里 TxtIconScaleValue 已经
            // Connect() 过了，以后用户自己拖滑块才会走这个处理器，不会在 InitializeComponent() 期间
            // 摸到还没就绪的控件
            SliderIconScale.ValueChanged += SliderIconScale_ValueChanged;

            Closed += (s, e) =>
            {
                var toSave = AppSettings.Load();
                toSave.Size = RbSizeSmall.IsChecked == true ? PlayerSize.Small
                            : RbSizeLarge.IsChecked == true ? PlayerSize.Large
                            : RbSizeCustom.IsChecked == true ? PlayerSize.Custom
                            : PlayerSize.Medium;
                // 填的不是数字/超出范围就悄悄夹回合法区间存起来，不弹错误吓跑人——这个输入框本来就是
                // "顺手填个数字"的地方，不是一个需要校验反馈的表单
                if (double.TryParse(TxtCustomWidth.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double customWidth))
                {
                    toSave.CustomWidth = System.Math.Clamp(customWidth, AppSettings.MinCustomWidth, AppSettings.MaxCustomWidth);
                }
                toSave.DisplayMode = RbModeMinimal.IsChecked == true ? PlayerDisplayMode.Minimal : PlayerDisplayMode.Standard;
                toSave.FontSize = RbFontSmall.IsChecked == true ? LyricFontSize.Small
                                 : RbFontLarge.IsChecked == true ? LyricFontSize.Large
                                 : LyricFontSize.Medium;
                toSave.IconScale = SliderIconScale.Value;
                toSave.Save();
            };
        }

        // 只有真的点了"自定义"才会触发（这个 RadioButton 没有显式写 IsChecked，InitializeComponent()
        // 期间不会有一次赋值触发的空跑）——选上就解锁宽度输入框，换到别的档位就锁回去，不让用户
        // 以为填了数字就生效，其实选的还是别的预设
        private void RbSizeCustom_CheckedChanged(object sender, RoutedEventArgs e) =>
            TxtCustomWidth.IsEnabled = RbSizeCustom.IsChecked == true;

        private void SliderIconScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
            UpdateIconScaleLabel(e.NewValue);

        // 主播放器窗口不会跟着这个滑块实时重画（这个页面是从首页 Hub 单独打开的，跟正在播放的
        // MainWindow 不是同一个实例，改完要回首页重新"开始播放"才会看到新图标大小生效）——这里更新
        // 百分比文字 + 右边那个预览图标的缩放，两个都是这个页面自己的东西，不需要碰到 MainWindow
        private void UpdateIconScaleLabel(double value)
        {
            TxtIconScaleValue.Text = $"{value * 100:0}%";
            IconScalePreviewTransform.ScaleX = value;
            IconScalePreviewTransform.ScaleY = value;
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e) => Close();
    }
}
