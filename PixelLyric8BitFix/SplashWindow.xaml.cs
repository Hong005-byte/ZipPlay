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
        private readonly WindowProceedGuard _nav;
        private DispatcherTimer? _runTimer;
        private BitmapSource? _runnerFrame1;
        private BitmapSource? _runnerFrame2;

        public SplashWindow()
        {
            InitializeComponent();

            _nav = new WindowProceedGuard(this);

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

        // 拉到中间之后，停顿一下让人看清 "ZIPPLAY"，再淡出切到下一步
        private void BeginExitSequence()
        {
            var pause = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
            pause.Tick += (s, e) =>
            {
                pause.Stop();
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(320));
                fadeOut.Completed += (s2, e2) => ProceedAfterSplash();
                RootGrid.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            };
            pause.Start();
        }

        // 开场动画放完之后去哪：
        //   没填过用户名 → 先问一下名字（WelcomeWindow），填完它自己会去开首页。这一条故意排在最前面——
        //   SkipStartupSettings 这个字段 1.x 就有、默认就是 true，老用户升级上来 UserName 是空的但
        //   SkipStartupSettings 已经是 true，如果先判断"要不要跳过"，这批人会直接进 MainWindow，
        //   永远走不到 WelcomeWindow，头像/昵称/分享卡片这些新功能等于白做
        //   有存档 && 勾了"跳过启动首页" → 直接拿上次的皮肤/尺寸/显示模式进播放器（老用户该多快还是多快）
        //   其它情况（比如手动关掉了"跳过"，但之前已经填过名字）→ 直接进首页（HomeWindow）
        // 想改设置随时能在主播放器窗口里右键 / 点齿轮回到首页，不会因为跳过这一步就再也找不到入口。
        private void ProceedAfterSplash()
        {
            var settings = AppSettings.Load();
            if (string.IsNullOrWhiteSpace(settings.UserName))
            {
                _nav.ProceedTo(new WelcomeWindow(isFirstRun: true));
            }
            else if (AppSettings.HasSavedConfig && settings.SkipStartupSettings)
            {
                _nav.ProceedTo(new MainWindow(settings));
            }
            else
            {
                _nav.ProceedTo(new HomeWindow());
            }
        }

        // 点一下跳过动画，直接进下一步——等不及看完整个开场也没关系
        private void Splash_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _runTimer?.Stop();
            ProceedAfterSplash();
        }
    }
}
