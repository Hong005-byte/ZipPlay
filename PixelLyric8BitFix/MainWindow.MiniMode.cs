using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Foundation;
using Windows.Media.Control;
using Forms = System.Windows.Forms;

namespace PixelLyric8BitFix
{
    // Mini 模式：双击窗口缩成一个贴合当前皮肤主题的小方块（还能拖着走），再点一下展开，回到缩小前的原位。
    public partial class MainWindow : Window
    {
        private void ToggleMiniMode()
        {
            if (_isMiniMode) ExitMiniMode();
            else EnterMiniMode();
        }

        // 缩成小方块：原地从中心缩小，而不是跳去左上角；缩小前的尺寸 + 位置都记下来，
        // 不管缩小之后把小方块拖去哪儿，展开时都精确回到这里记的原位（而不是拖到哪儿从哪儿展开）。
        // 窗口实际缩成的是 MiniModeWindowSize（比方块本体 MiniBadgeSize 大一整圈），多出来的部分
        // 铺满像素粒子背景，方块本身视觉大小不变。
        private void EnterMiniMode()
        {
            if (_isMiniMode) return;
            _isMiniMode = true;

            _preMiniWidth = Width;
            _preMiniHeight = Height;
            _preMiniLeft = Left;
            _preMiniTop = Top;

            double centerX = Left + Width / 2;
            double centerY = Top + Height / 2;

            MainContentGrid.Visibility = Visibility.Collapsed;
            TopLeftIconsPanel.Visibility = Visibility.Collapsed; // 齿轮 + 卡拉OK/双语开关，小方块状态下用不上也放不下
            UpdateBadge.Visibility = Visibility.Collapsed;

            ApplyMiniBadgeAppearance(_settings.Skin);
            MiniBadge.Visibility = Visibility.Visible;

            EnsureMiniVisualizerParticlesBuilt();
            RefreshAchievementRing();
            MiniVisualizerCanvas.Visibility = Visibility.Visible;

            Width = MiniModeWindowSize;
            Height = MiniModeWindowSize;
            Left = centerX - MiniModeWindowSize / 2;
            Top = centerY - MiniModeWindowSize / 2;

            SyncAudioVisualizerState();
        }

        // 展开：不看小方块现在被拖到哪了，直接照搬 EnterMiniMode 时存的原始位置/尺寸
        private void ExitMiniMode()
        {
            if (!_isMiniMode) return;
            _isMiniMode = false;

            // 双击展开这个动作现在有两条独立路径都可能触发它：MiniBadge 自己的
            // MiniBadge_MouseLeftButtonUp，以及 Window 级别的 MouseDoubleClick（MainWindow.xaml.cs 里
            // 的 ToggleMiniMode，那个识别发生在隧道阶段，可能在 MiniBadge 自己的 MouseLeftButtonDown/Up
            // 跑完之前就先触发）——真遇到这种时序，MiniBadge 手上可能还"捏着"一次没结束的拖拽手势
            // （_miniBadgeDragCaptured 还是 true），这里先干净地放手，不留一个指向即将隐藏的元素的
            // 鼠标捕获，也不留一个再也等不到 Up 事件去复位的旧标记
            if (_miniBadgeDragCaptured)
            {
                _miniBadgeDragCaptured = false;
                MiniBadge.ReleaseMouseCapture();
            }

            MiniBadge.Visibility = Visibility.Collapsed;
            MiniVisualizerCanvas.Visibility = Visibility.Collapsed;
            HidePetBubble(); // 展开的时候气泡还开着就很奇怪——桌宠那部分场景已经不存在了
            MainContentGrid.Visibility = Visibility.Visible;
            TopLeftIconsPanel.Visibility = Visibility.Visible;
            UpdateBadge.Visibility = _updateInfo != null ? Visibility.Visible : Visibility.Collapsed;

            Width = _preMiniWidth;
            Height = _preMiniHeight;
            Left = _preMiniLeft;
            Top = _preMiniTop;

            SyncAudioVisualizerState();
        }

        // 现在有两个可能想要音频数据的消费者：Mini 模式的像素粒子、皮肤音乐律动（黑胶/磁带机/篝火/
        // Minecraft/星空/雨夜/极光雪夜/樱花/CRT/赛博朋克 + 客制化主题，见 MainWindow.SkinInteractions.cs）。
        // 只要其中一个的开关开着、且各自要求的状态满足，加上窗口没被隐藏到托盘，就该抓；两个都不需要
        // 的时候才停——省资源，跟这个 app 别的地方（SMTC 那套锚点插值、抓词的短超时）一个思路。
        // 两个开关都关掉的话压根不会启动系统音频采集。
        private void SyncAudioVisualizerState()
        {
            bool miniVisualizerWantsAudio = _settings.MiniVisualizerEnabled && _isMiniMode;
            bool skinReactiveWantsAudio = _isMusicReactiveSkin && _settings.SkinAudioReactiveEnabled;
            bool shouldRun = (miniVisualizerWantsAudio || skinReactiveWantsAudio) && IsVisible;

            if (shouldRun) _audioVisualizer.Start();
            else _audioVisualizer.Stop();
        }

        // 每颗粒子的基础边长/圆角，实际大小靠 ScaleTransform 缩放；圆边而不是尖角，
        // 跟方块本体、歌词框这些别的地方一样是圆角风格，不是硬邦邦的正方形
        private const double MiniVisualizerCellSize = 11;
        private const double MiniVisualizerCornerRadius = 3;

        // 内圈紧贴方块外沿（方块半径 32，留一点缝隙），外圈离得更远一截，两圈之间留出明显的空隙，
        // 这样"外圈多了一圈"这件事才看得出来，不会跟内圈糊成一片
        private const double MiniVisualizerInnerRingRadius = 42;
        private const double MiniVisualizerOuterRingRadius = 58;
        private const int MiniVisualizerInnerRingCount = 20;
        private const int MiniVisualizerOuterRingCount = 26; // 外圈颗数更多一点，真的亮起来的时候视觉分量能跟"多一圈"这个说法对上

        // 两圈同心的像素粒子环：内圈跟着整体响度走，外圈只在鼓点/高音冲击时出现，具体逻辑在 UpdateMiniVisualizer。
        // 只在第一次进 Mini 模式时生成，之后反复进出 Mini 模式直接复用。颜色只在生成那一刻按当前皮肤定一次——
        // 同一个 MainWindow 实例存活期间皮肤不会变（真要换皮肤得从右键菜单回设置页，那边会整个重新开一个新的
        // MainWindow），不用考虑运行中变色。
        private void EnsureMiniVisualizerParticlesBuilt()
        {
            if (_miniVisualizerParticles != null) return;

            double center = MiniModeWindowSize / 2;
            var brush = new SolidColorBrush(GetActiveSkinTheme(_settings.Skin).MiniBorder);
            var particles = new List<(System.Windows.Shapes.Rectangle Element, bool IsOuterRing)>();

            BuildRing(particles, brush, center, MiniVisualizerInnerRingRadius, MiniVisualizerInnerRingCount, isOuterRing: false);
            BuildRing(particles, brush, center, MiniVisualizerOuterRingRadius, MiniVisualizerOuterRingCount, isOuterRing: true);

            foreach (var (element, _) in particles) MiniVisualizerCanvas.Children.Add(element);
            _miniVisualizerParticles = particles.ToArray();
        }

        private static void BuildRing(
            List<(System.Windows.Shapes.Rectangle Element, bool IsOuterRing)> particles,
            Brush brush, double center, double radius, int count, bool isOuterRing)
        {
            for (int i = 0; i < count; i++)
            {
                double angle = i * (2 * Math.PI / count);
                double cx = center + radius * Math.Cos(angle);
                double cy = center + radius * Math.Sin(angle);

                var particle = new System.Windows.Shapes.Rectangle
                {
                    Width = MiniVisualizerCellSize,
                    Height = MiniVisualizerCellSize,
                    RadiusX = MiniVisualizerCornerRadius,
                    RadiusY = MiniVisualizerCornerRadius,
                    Fill = brush,
                    Opacity = 0, // 待机时完全不显示，只有真的跳起来才冒出来，见 UpdateMiniVisualizer
                    RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                    RenderTransform = new ScaleTransform(0.3, 0.3),
                };
                RenderOptions.SetBitmapScalingMode(particle, System.Windows.Media.BitmapScalingMode.NearestNeighbor);
                Canvas.SetLeft(particle, cx - MiniVisualizerCellSize / 2);
                Canvas.SetTop(particle, cy - MiniVisualizerCellSize / 2);
                particles.Add((particle, isOuterRing));
            }
        }

        // 成就环：绕在音频粒子外圈（半径 58）再外面一圈的 8 个小方点，静态显示 7 个常规听歌成就 +
        // 1 个压轴（尊贵听众）分别解没解锁——不吃音频数据，纯粹是"把成就墙那个隐性的数字变成一直
        // 挂在眼前的反馈"。半径卡在 65（窗口半宽只有 70，再往外挤点数就快贴到窗口边缘被裁掉了），
        // 点比音频粒子小一圈（6px vs 11px），视觉上一眼能分清"这是另一种东西，不是律动的一部分"。
        private const double MiniAchievementRingRadius = 65;
        private const double MiniAchievementDotSize = 6;

        // 每次真正"进入" Mini 模式都重新算一遍（不是跟音频粒子环一样只建一次全程复用）——两次进
        // Mini 模式之间完全可能新解锁了成就（这段时间一直在正常模式下听歌），旧的环形状不该继续显示。
        // 用的是 MainWindow 自己内存里那份 _stats（实时累计中的听歌数据，见 MainWindow.ListeningStats.cs），
        // 不用重新读一遍盘，状态已经是当下最新的。具体每个点画在哪、什么颜色/透明度交给
        // MiniAchievementRing.BuildDots 这个纯函数算，这里只管把算出来的结果变成真的 Rectangle。
        private void RefreshAchievementRing()
        {
            foreach (var dot in _miniAchievementDots) MiniVisualizerCanvas.Children.Remove(dot);
            _miniAchievementDots.Clear();

            var progress = AchievementCalculator.Evaluate(_stats);
            double center = MiniModeWindowSize / 2;

            foreach (var spec in MiniAchievementRing.BuildDots(progress, center, MiniAchievementRingRadius))
            {
                var dot = new System.Windows.Shapes.Rectangle
                {
                    Width = MiniAchievementDotSize,
                    Height = MiniAchievementDotSize,
                    // 圆角半径 = 边长一半，画出来是个正圆——跟音频粒子那种"圆角方块"（RadiusX/Y=3，
                    // 11px 边长）在形状上也刻意区分开，不只是颜色不一样，两个信号来源看一眼就能分清
                    RadiusX = MiniAchievementDotSize / 2,
                    RadiusY = MiniAchievementDotSize / 2,
                    Fill = new SolidColorBrush(spec.Color),
                    Opacity = spec.Opacity,
                };
                RenderOptions.SetBitmapScalingMode(dot, System.Windows.Media.BitmapScalingMode.NearestNeighbor);
                Canvas.SetLeft(dot, spec.CenterX - MiniAchievementDotSize / 2);
                Canvas.SetTop(dot, spec.CenterY - MiniAchievementDotSize / 2);
                MiniVisualizerCanvas.Children.Add(dot);
                _miniAchievementDots.Add(dot);
            }
        }

        // 能量低于这个值就完全不显示——没有这道门槛的话，静音/底噪时的浮点误差也会有个接近 0
        // 但不是 0 的值，看起来就是"这一圈一直若隐若现"。现在改成只有真的跳到这个阈值以上才会冒出来。
        // 内圈用的"整体响度"是 16 个频段平均出来的，本身波动就比单个频段柔和很多，门槛不能定太高，
        // 不然正常播放音乐时也很难跳过去——这也是上一版看起来完全不动的主因之一。
        private const float MiniVisualizerVisibilityThreshold = 0.05f;

        // 刚越过门槛那一刻的起始亮度——不从 0 开始慢慢爬，直接就有个能看清的亮度，"冒出来"的观感更明显
        private const double MiniVisualizerAppearOpacity = 0.55;

        // 每 tick（50ms）读一份最新的整体响度 + 节奏冲击快照，分别驱动内圈/外圈——纯数据驱动，
        // 跟主歌词那边"只有分割点变了才重排版"的思路不同：这里视觉上就是要连续跳动，直接每帧写没问题
        private void UpdateMiniVisualizer()
        {
            if (_miniVisualizerParticles == null) return;

            var snapshot = _audioVisualizer.GetSnapshot();
            foreach (var (element, isOuterRing) in _miniVisualizerParticles)
            {
                float level = isOuterRing ? snapshot.BeatPulse : snapshot.OverallLevel;
                ApplyParticleLevel(element, level);
            }
        }

        private static void ApplyParticleLevel(System.Windows.Shapes.Rectangle element, float level)
        {
            if (level < MiniVisualizerVisibilityThreshold)
            {
                element.Opacity = 0; // 没跳到这个门槛，直接不显示，不是"暗一点"而是彻底没有
                return;
            }

            double scale = 0.3 + level * 1.5;   // 0.3x（刚冒出来）~ 1.8x（能量拉满）
            var transform = (ScaleTransform)element.RenderTransform;
            transform.ScaleX = scale;
            transform.ScaleY = scale;

            // 超过门槛之后，把 [threshold, 1] 这段能量重新映射到 [MiniVisualizerAppearOpacity, 1] 的亮度区间
            double normalized = (level - MiniVisualizerVisibilityThreshold) / (1 - MiniVisualizerVisibilityThreshold);
            element.Opacity = Math.Clamp(MiniVisualizerAppearOpacity + normalized * (1 - MiniVisualizerAppearOpacity), 0, 1);
        }

        // 小方块的配色/图标跟当前皮肤走：有现成像素图标（树/篝火/咖啡杯）的就用像素图，
        // 其余皮肤先用 README 里已经在用的那个 emoji 顶一下，观感也算统一
        private void ApplyMiniBadgeAppearance(PlayerSkin skin)
        {
            var t = GetActiveSkinTheme(skin);
            MiniBadge.Background = new SolidColorBrush(t.MiniBg);
            MiniBadge.BorderBrush = new SolidColorBrush(t.MiniBorder);
            MiniBadgeImage.Source = t.MiniIcon();
        }

        // 小方块本身也能拖着走。之前这里借用 Window.DragMove()（原生非客户区拖拽循环），
        // 靠"松手后比一下位置有没有变"判断是不是纯点击，双击靠 e.ClickCount 判断——这两者都栽了：
        // DragMove() 内部是 SendMessage(WM_SYSCOMMAND, SC_MOUSEMOVE) 发起的系统级拖动，第一下点击
        // 的按下→抬起整个被这个非客户区循环吞掉，不走 WPF 正常的鼠标消息管线，于是 WPF 自己算
        // ClickCount 用的"上一次点击时间/位置"状态是错的——实测真双击经常两次都量成 ClickCount==1，
        // 永远走不到 ExitMiniMode()，双击卡在只有反应气泡、回不去主界面。中途换成自己按时间戳判定
        // 双击，但 DragMove() 本身还留着，没有排除"这套系统级拖动循环还有别的方式在吞第二次点击"
        // 的可能——用户反馈换了时间戳判定之后问题依然存在，说明病根真的是 DragMove()/SC_MOUSEMOVE
        // 这条路径本身，不是"怎么判定双击"这个细节。
        //
        // 现在整个换成手写拖拽（MouseMove + CaptureMouse，不调用 DragMove()/SC_MOUSEMOVE）——按下
        // 只记起点、抬手才真正判断这是点击还是拖拽，中途完全不触碰任何系统级拖动 API，WPF 的鼠标
        // 消息管线（包括 ClickCount 这套内部状态）全程正常运作，不会被打断。
        //
        // 位移全程只用 e.GetPosition(this)（窗口自己的本地坐标系），不经过 PointToScreen/屏幕坐标——
        // WPF 的 PointToScreen 在多显示器 + 每显示器独立 DPI 缩放的场景下是有名的换算不准的坑
        // （这也是当初选 DragMove() 的原因之一，见上面的旧注释）。留在窗口本地坐标系里完全绕开这个
        // 坑：_miniBadgeDragStartPoint 记的是"按下时鼠标相对窗口的位置"，之后每次 MouseMove 再量一次
        // 鼠标相对窗口的位置，两次的差值就是窗口该挪动的量（因为窗口边框相对客户区的偏移是常数，
        // 做差会自动抵消，不用关心具体偏移量是多少）；'this'（窗口）挪动之后，下一次 GetPosition 会
        // 用新的窗口位置重新计算，这个式子每次都重新成立，不会累积误差。跟 IconPainterWindow 帧缩略图
        // 拖拽重排（FrameStripPanel_MouseMove 的阈值判定）是同一个思路，这个代码库里已经验证过好用。
        private bool _miniBadgeDragCaptured;
        private System.Windows.Point _miniBadgeDragStartPoint; // "抓取点"相对窗口的位置，按下时定住，中途不再更新
        private bool _miniBadgeDragMoved;
        private int _miniBadgeClickCountOnDown; // ClickCount 只有在按下的那一刻可靠，先记下来，抬手时再用
        private const double MiniBadgeDragThreshold = 4; // 像素，比 IconPainterWindow 帧缩略图拖拽判定的 6 略紧一点——方块本身不大，不需要留太多容错

        private void MiniBadge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            _miniBadgeClickCountOnDown = e.ClickCount;
            _miniBadgeDragStartPoint = e.GetPosition(this);
            _miniBadgeDragMoved = false;
            _miniBadgeDragCaptured = true;
            MiniBadge.CaptureMouse();
        }

        private void MiniBadge_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_miniBadgeDragCaptured || e.LeftButton != MouseButtonState.Pressed) return;

            var current = e.GetPosition(this);
            double dx = current.X - _miniBadgeDragStartPoint.X;
            double dy = current.Y - _miniBadgeDragStartPoint.Y;

            if (!_miniBadgeDragMoved && (Math.Abs(dx) > MiniBadgeDragThreshold || Math.Abs(dy) > MiniBadgeDragThreshold))
                _miniBadgeDragMoved = true; // 挪够距离才算真的在拖，不然手一抖的小幅度移动会让单击/双击变成一次没意义的"拖了 0px"

            if (_miniBadgeDragMoved)
            {
                Left += dx;
                Top += dy;
            }
        }

        // 拖了 → 什么都不做（跟以前一样，位置已经在 MouseMove 里实时更新过了）。没拖 + 单击 → 桌宠
        // 反应（气泡+弹一下），不再直接展开；没拖 + 双击 → 展开，这是以前"单击展开"那个行为搬过来的。
        // 双击判定直接用按下时记的 e.ClickCount——没有 DragMove() 搅局，WPF 自己这套计数现在是准的，
        // 不需要再手动按时间戳重新发明一遍。
        private void MiniBadge_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_miniBadgeDragCaptured) return;
            _miniBadgeDragCaptured = false;
            MiniBadge.ReleaseMouseCapture();

            if (_miniBadgeDragMoved) return;

            bool isDoubleClick = _miniBadgeClickCountOnDown >= 2;
            // 关掉这个开关的话，单击也跟着退回最初的"点一下直接展开"
            if (!_settings.MiniPetReactionEnabled || isDoubleClick) ExitMiniMode();
            else ShowPetReaction();
        }
    }
}
