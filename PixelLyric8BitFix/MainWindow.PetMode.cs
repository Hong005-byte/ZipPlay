using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace PixelLyric8BitFix
{
    // Mini 模式小方块单击的"桌宠"反应——弹一句话（听歌统计/时间段问候，复用 HomeGreetingBuilder
    // 那套文案池）+ 弹一下动画。双击展开这个已有行为保留在 MainWindow.MiniMode.cs 里。
    //
    // 这个文件原来还带一套"小方块自己在屏幕上走来走去"的逻辑，用户体验过一轮之后觉得一直跑来跑去
    // 太吵了，撤掉了——单击反应这部分体验是好的，保留。如果以后想做"走动"，方向是"皮肤/客制化主题
    // 的图标在自己的显示区域里走"（像 Minecraft 皮肤的 Steve、城市夜景皮肤的地铁那样），不是"整个
    // Mini 窗口在桌面上到处跑"，那个已经证明体验不好。
    public partial class MainWindow : Window
    {
        private const double PetBubbleHideSeconds = 3.5;  // 比 ShowToast 的 1.5 秒长——气泡通常是一整句话

        private readonly Random _petRandom = new();
        private DispatcherTimer? _petBubbleHideTimer;

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
