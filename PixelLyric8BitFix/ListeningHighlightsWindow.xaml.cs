using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// "✨ 查看亮点回顾"的展示窗口——把 ListeningHighlightsBuilder 拆出来的几张卡片一张张翻，
    /// 纯展示，不碰磁盘/网络。卡片配色跟着调用方传进来的主题走（跟 ShareCardBuilder 分享卡片同一个
    /// "卡片风格不一定是当前正在用的皮肤，是 ListeningStatsWindow 那个下拉框选的哪套"的规则）。
    /// </summary>
    public partial class ListeningHighlightsWindow : Window
    {
        private static readonly Color CardBg = Color.FromRgb(0x12, 0x16, 0x0F);

        private readonly List<ListeningHighlightsBuilder.HighlightSlide> _slides;
        private readonly Color _accent;
        private int _index;

        // internal 而不是 public：SkinTheme 本身是 internal 类型，构造函数入参类型的可见性不能比
        // 构造函数本身更窄——跟 ListeningStatsWindow.ResolveSelectedCardTheme() 返回 SkinTheme
        // 是同一个道理，两边都在同一个程序集里，不需要真的对外公开
        internal ListeningHighlightsWindow(ListeningSummary summary, string periodLabel, string? userName, SkinTheme theme)
        {
            InitializeComponent();

            _accent = theme.Accent;
            _slides = ListeningHighlightsBuilder.BuildSlides(summary, periodLabel, userName);

            SlideCard.Background = new SolidColorBrush(CardBg);
            SlideCard.BorderBrush = new SolidColorBrush(_accent);

            RenderSlide();
        }

        private void RenderSlide()
        {
            var slide = _slides[_index];
            TxtSlideIcon.Text = slide.Icon;
            TxtSlideHeadline.Text = slide.Headline;
            TxtSlideHeadline.Foreground = new SolidColorBrush(_accent);
            TxtSlideSubtext.Text = slide.Subtext;
            TxtSlideIndicator.Text = $"{_index + 1} / {_slides.Count}";

            BtnPrev.IsEnabled = _index > 0;
            // 最后一张的"下一张"直接变成"关闭"，不用再单独放一个"完成"按钮占地方——
            // 翻到头了自然就是该收尾了
            BtnNext.Content = _index == _slides.Count - 1 ? "✅ 关闭" : "下一张 →";
        }

        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            if (_index <= 0) return;
            _index--;
            RenderSlide();
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (_index < _slides.Count - 1)
            {
                _index++;
                RenderSlide();
                return;
            }
            Close();
        }
    }
}
