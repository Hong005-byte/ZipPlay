using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace PixelLyric8BitFix
{
    // "桌宠"：Mini 模式小方块平时不是纹丝不动的——隔一阵子自己在当前位置附近挑个新点、慢慢走过去；
    // 单击会弹一句话（听歌统计/时间段问候，复用 HomeGreetingBuilder 那套文案池）+ 弹一下动画。
    // 双击展开这个已有行为保留在 MainWindow.MiniMode.cs 里，这个文件只管"平时自己动"+"单击的反应"。
    //
    // 走动范围故意不接 WorkArea/多屏 DPI 换算那一套——每次都是"当前位置 ± PetWanderRadius 内随机
    // 一个点"，只拿 SystemParameters.VirtualScreenLeft/Top/Width/Height（RestoreWindowPosition 已经在
    // 用的同一组属性）做个兜底夹一下，保证不会走出整个虚拟桌面。这样天然支持多屏——桌宠就在用户
    // 放它的那块屏幕附近打转，不需要知道"这是哪个屏幕、任务栏在哪"；代价是走位可能会跟任务栏
    // 的显示层级打架（视觉上被任务栏盖住一截），这是本阶段接受的小瑕疵。
    public partial class MainWindow : Window
    {
        private const double PetWanderRadius = 220;      // 每次挑新目标点，离当前位置最远多少像素
        private const double PetWalkSpeedPerSecond = 40;  // 散步速度，像素/秒
        private const int PetIdleMinSeconds = 15;
        private const int PetIdleMaxSeconds = 45;
        private const double PetBubbleHideSeconds = 3.5;  // 比 ShowToast 的 1.5 秒长——气泡通常是一整句话

        private readonly Random _petRandom = new();
        private bool _isDraggingMiniBadge;   // MiniBadge_MouseLeftButtonDown 拖拽期间置 true，UpdatePetWander 让路
        private bool _petIsWalking;
        private double _petTargetLeft;
        private double _petTargetTop;
        private DateTimeOffset _petIdleUntil;
        private DispatcherTimer? _petBubbleHideTimer;

        // 从 SmoothTimer_Tick 里跟 UpdateMiniVisualizer 并列调用，条件（是否 Mini 模式/开关开没开/
        // 是不是正在被拖）由调用方判断，这里只管"该走了就走，该等就等"这一件事
        private void UpdatePetWander()
        {
            if (_petIsWalking)
            {
                StepTowardPetTarget();
                return;
            }

            if (DateTimeOffset.Now >= _petIdleUntil)
            {
                PickNewPetTarget();
                _petIsWalking = true;
            }
        }

        private void PickNewPetTarget()
        {
            double left = SystemParameters.VirtualScreenLeft;
            double top = SystemParameters.VirtualScreenTop;
            double right = left + SystemParameters.VirtualScreenWidth;
            double bottom = top + SystemParameters.VirtualScreenHeight;

            double candidateLeft = Left + (_petRandom.NextDouble() * 2 - 1) * PetWanderRadius;
            double candidateTop = Top + (_petRandom.NextDouble() * 2 - 1) * PetWanderRadius;

            _petTargetLeft = Math.Clamp(candidateLeft, left, Math.Max(left, right - Width));
            _petTargetTop = Math.Clamp(candidateTop, top, Math.Max(top, bottom - Height));
        }

        // 每 tick（50ms）朝目标点方向挪一小步——用方向向量归一化，斜着走跟直着走速度一样，
        // 不会出现"斜线走得比直线快"这种观感上的小别扭
        private void StepTowardPetTarget()
        {
            double dx = _petTargetLeft - Left;
            double dy = _petTargetTop - Top;
            double distance = Math.Sqrt(dx * dx + dy * dy);

            // 50 = _smoothTimer 的 tick 间隔（毫秒），跟 MainWindow.xaml.cs 构造函数里
            // `_smoothTimer.Interval = TimeSpan.FromMilliseconds(50)` 那一行手动对应——那边不是个
            // 命名常量，这里就不额外造一个容易跟那边脱节的"共享常量"，直接写死并在这标注清楚
            double stepSize = PetWalkSpeedPerSecond * (50.0 / 1000.0);
            if (distance <= stepSize)
            {
                Left = _petTargetLeft;
                Top = _petTargetTop;
                _petIsWalking = false;
                ResetPetWanderIdle();
                return;
            }

            Left += dx / distance * stepSize;
            Top += dy / distance * stepSize;
        }

        // 重新掷一次待机倒计时——进 Mini 模式的那一刻、每次走完一趟之后都要重掷，不然要么一进
        // Mini 模式就立刻开始走，要么走完立刻又走
        private void ResetPetWanderIdle()
        {
            _petIsWalking = false;
            _petIdleUntil = DateTimeOffset.Now + TimeSpan.FromSeconds(_petRandom.Next(PetIdleMinSeconds, PetIdleMaxSeconds + 1));
        }

        // 单击触发：文案复用 HomeGreetingBuilder 那套"时间段 + 偶尔听歌数据"的文案池（suffix 传空——
        // 首页那句"点这里改资料"对着一个点了会弹反应的小方块没有意义），弹出气泡 + 让方块弹一下
        private void ShowPetReaction()
        {
            string message = HomeGreetingBuilder.Build(DateTime.Now, _settings.UserName, _stats, _petRandom, suffix: "");
            TxtPetBubble.Text = message;

            var theme = GetActiveSkinTheme(_settings.Skin);
            PetBubbleBorder.BorderBrush = new SolidColorBrush(theme.MiniBorder);

            PetBubblePopup.IsOpen = true;

            _petBubbleHideTimer?.Stop();
            _petBubbleHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(PetBubbleHideSeconds) };
            _petBubbleHideTimer.Tick += (s, e) =>
            {
                PetBubblePopup.IsOpen = false;
                _petBubbleHideTimer?.Stop();
            };
            _petBubbleHideTimer.Start();

            // 照抄 PlaySteveJump/PlayUfoHop 的写法——同一个通用的"弹一下再回位"动画，只是这次作用在
            // MiniBadge 自己的位移变换上，不是某个皮肤专属装饰的变换
            PlayBeatBounce(MiniBadgeBounceTransform, TranslateTransform.YProperty, -10, 150);
        }

        // 展开回完整播放器的时候调用——桌宠那部分场景已经不存在了，气泡还开着会很奇怪
        private void HidePetBubble()
        {
            _petBubbleHideTimer?.Stop();
            PetBubblePopup.IsOpen = false;
        }
    }
}
