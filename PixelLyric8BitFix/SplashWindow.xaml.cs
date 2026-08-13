using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 开场动画：小人从右边把 ZIPPLAY 的像素字拖进画面中央，停一下，淡出后进入设置页。
    /// 跟设置页一样用 ShutdownMode="OnExplicitShutdown"，被强行关掉（Alt+F4）时要自己退出整个 App。
    /// </summary>
    public partial class SplashWindow : Window
    {
        private bool _proceeding = false;
        private DispatcherTimer? _runTimer;
        private BitmapSource? _runnerFrame1;
        private BitmapSource? _runnerFrame2;

        public SplashWindow()
        {
            InitializeComponent();

            Closed += (s, e) =>
            {
                if (!_proceeding) Application.Current.Shutdown();
            };

            _runnerFrame1 = PixelArt.CreateRunnerFrame1();
            _runnerFrame2 = PixelArt.CreateRunnerFrame2();
            ImgRunner.Source = _runnerFrame1;
            ImgLogo.Source = PixelArt.CreateZipPlayLogo();

            Loaded += SplashWindow_Loaded;
        }

        private void SplashWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 跑步换腿，比主播放器里 Steve 走路快一点，更有"冲刺拖拽"的感觉
            _runTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            _runTimer.Tick += (s, args) =>
            {
                ImgRunner.Source = ReferenceEquals(ImgRunner.Source, _runnerFrame1) ? _runnerFrame2 : _runnerFrame1;
            };
            _runTimer.Start();

            var pullIn = new DoubleAnimation
            {
                From = 700,
                To = 0,
                Duration = TimeSpan.FromSeconds(1.1),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            pullIn.Completed += (s, args) =>
            {
                _runTimer?.Stop();
                BeginExitSequence();
            };
            GroupTransform.BeginAnimation(TranslateTransform.XProperty, pullIn);
        }

        // 拉到中间之后，停顿一下让人看清 "ZIPPLAY"，再淡出切到设置页
        private void BeginExitSequence()
        {
            var pause = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
            pause.Tick += (s, e) =>
            {
                pause.Stop();
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(320));
                fadeOut.Completed += (s2, e2) => GoToSettings();
                RootGrid.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            };
            pause.Start();
        }

        private void GoToSettings()
        {
            _proceeding = true;

            var settings = new SettingsWindow();
            Application.Current.MainWindow = settings;
            settings.Show();

            Close();
        }

        // 点一下跳过动画，直接进设置页——等不及看完整个开场也没关系
        private void Splash_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _runTimer?.Stop();
            GoToSettings();
        }
    }
}
