using System.Collections.Generic;
using Microsoft.UI.Xaml.Navigation;
using PixelLyric8BitFix;

namespace PixelLyric8Bit.Mobile;

/// <summary>
/// ✨ 亮点回顾——把 StatsPage 传过来的一份 HighlightSlide 列表（纯逻辑在 Core 的
/// ListeningHighlightsBuilder，跟桌面版 ListeningHighlightsWindow 用的是同一份，这边只负责"一张
/// 卡片说一件事、点按钮翻页"这个交互）挨个显示出来。卡片列表是 Frame.Navigate 的导航参数直接传
/// 过来的（同进程内的对象引用，不用序列化），不是这个页面自己算的——StatsPage 已经按当前选中的
/// 时间范围（本月/今年/全部时间）算好了 ListeningSummary 再转成卡片，这边只管翻页显示。
/// </summary>
public sealed partial class HighlightsPage : Page
{
    private List<ListeningHighlightsBuilder.HighlightSlide> _slides = new();
    private int _index;

    public HighlightsPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _slides = e.Parameter as List<ListeningHighlightsBuilder.HighlightSlide> ?? new();
        _index = 0;
        RenderSlide();
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    private void RenderSlide()
    {
        if (_slides.Count == 0)
        {
            TxtSlideIcon.Text = "🤷";
            TxtSlideHeadline.Text = "还没有数据";
            TxtSlideSubtext.Text = "悬浮窗打开、真的在放歌的时候才会开始攒听歌统计。";
            TxtPageIndicator.Text = "0 / 0";
            BtnPrev.IsEnabled = false;
            BtnNext.IsEnabled = false;
            return;
        }

        var slide = _slides[_index];
        TxtSlideIcon.Text = slide.Icon;
        TxtSlideHeadline.Text = slide.Headline;
        TxtSlideSubtext.Text = slide.Subtext;
        TxtPageIndicator.Text = $"{_index + 1} / {_slides.Count}";
        BtnPrev.IsEnabled = _index > 0;
        BtnNext.IsEnabled = _index < _slides.Count - 1;
    }

    private void BtnPrev_Click(object sender, RoutedEventArgs e)
    {
        if (_index > 0) { _index--; RenderSlide(); }
    }

    private void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        if (_index < _slides.Count - 1) { _index++; RenderSlide(); }
    }
}
