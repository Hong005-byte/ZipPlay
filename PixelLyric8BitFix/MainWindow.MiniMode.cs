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

            MiniBadge.Visibility = Visibility.Collapsed;
            MiniVisualizerCanvas.Visibility = Visibility.Collapsed;
            MainContentGrid.Visibility = Visibility.Visible;
            TopLeftIconsPanel.Visibility = Visibility.Visible;
            UpdateBadge.Visibility = _updateInfo != null ? Visibility.Visible : Visibility.Collapsed;

            Width = _preMiniWidth;
            Height = _preMiniHeight;
            Left = _preMiniLeft;
            Top = _preMiniTop;

            SyncAudioVisualizerState();
        }

        // 只有"设置页开着这个功能 且 处于 Mini 状态 且 窗口没被隐藏到托盘"这三个条件同时满足
        // 才值得抓音频/算 FFT，缺一个都该停——省资源，跟这个 app 别的地方（SMTC 那套锚点插值、
        // 抓词的短超时）一个思路；设置页把开关关掉的话，Mini 模式下压根不会启动系统音频采集
        private void SyncAudioVisualizerState()
        {
            if (_settings.MiniVisualizerEnabled && _isMiniMode && IsVisible) _audioVisualizer.Start();
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

        // 小方块本身也能拖着走：借用 DragMove() 的原生拖拽循环（跟窗口其它地方拖拽同一套机制，
        // 不用自己手撸鼠标坐标换算，天然兼容多显示器/缩放）。DragMove() 会一直阻塞到用户松开左键，
        // 返回后比较一下位置有没有变化——没变就是单纯点了一下，没有拖动，那就当成"点击展开"。
        private void MiniBadge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            double beforeLeft = Left, beforeTop = Top;
            this.DragMove();
            bool wasDragged = Math.Abs(Left - beforeLeft) > 1 || Math.Abs(Top - beforeTop) > 1;
            if (!wasDragged) ExitMiniMode();
        }
    }
}
