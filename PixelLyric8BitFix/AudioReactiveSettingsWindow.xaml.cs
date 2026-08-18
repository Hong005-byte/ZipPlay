using System.Windows;

namespace PixelLyric8BitFix
{
    /// <summary>Mini 像素粒子开关+灵敏度、皮肤音乐律动开关——从 HomeWindow 的"🎵 音频律动"格子进来，
    /// 打开读一次 AppSettings，关闭时存回去。</summary>
    public partial class AudioReactiveSettingsWindow : Window
    {
        public AudioReactiveSettingsWindow()
        {
            InitializeComponent();

            var settings = AppSettings.Load();
            ChkMiniVisualizerEnabled.IsChecked = settings.MiniVisualizerEnabled;
            RbVisualizerLow.IsChecked = settings.VisualizerSensitivity == VisualizerSensitivity.Low;
            RbVisualizerMedium.IsChecked = settings.VisualizerSensitivity == VisualizerSensitivity.Medium;
            RbVisualizerHigh.IsChecked = settings.VisualizerSensitivity == VisualizerSensitivity.High;
            ChkSkinAudioReactiveEnabled.IsChecked = settings.SkinAudioReactiveEnabled;

            Closed += (s, e) =>
            {
                var toSave = AppSettings.Load();
                toSave.MiniVisualizerEnabled = ChkMiniVisualizerEnabled.IsChecked == true;
                toSave.VisualizerSensitivity = RbVisualizerLow.IsChecked == true ? VisualizerSensitivity.Low
                                              : RbVisualizerHigh.IsChecked == true ? VisualizerSensitivity.High
                                              : VisualizerSensitivity.Medium;
                toSave.SkinAudioReactiveEnabled = ChkSkinAudioReactiveEnabled.IsChecked == true;
                toSave.Save();
            };
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e) => Close();
    }
}
