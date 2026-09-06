using System.Linq;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using PixelLyric8BitFix;

namespace PixelLyric8Bit.Mobile;

/// <summary>
/// 听歌统计 + 成就墙——本月/今年/全部时间切换、热门艺人/歌曲榜、🔥 听歌热力图、✨ 亮点回顾、成就
/// checklist，跟桌面版 ListeningStatsWindow 基本对齐；没有的是热力图之外"生成分享卡片"那张 PNG图
/// （见 ShareCardBuilder，渲染这一步各平台各写各的，还没接）。这几样背后的纯计算全部是
/// PixelLyric8Bit.Core 里早就写好、桌面版也在用的同一份代码（ListeningStatsAggregator/
/// ListeningHeatmap/ListeningHighlightsBuilder），这个页面只管把结果画出来。
///
/// 统计数据是 FloatingOverlayService 在悬浮窗那边攒、存盘的，这个页面完全不采集——两边是不同的
/// 组件（Service vs. Page），没有共享内存状态，只有同一份磁盘文件是两边的共同点，所以这里用定时器
/// 隔几秒重新读一次文件，不是订阅什么变更通知（跟皮肤那种"同进程内 SharedPreferences 监听"不是
/// 一回事，读文件本身足够便宜，没必要为了这个再多做一层通知机制）。
///
/// NavigationCacheMode=Required 保证这个定时器只会有一份在跑——不设的话每次从首页点进来都是一个
/// 新的页面实例、一个新的定时器，旧的定时器（连同它引用的旧页面实例）不会自动停，会一直在后台
/// 每 3 秒读一次文件，攒得越多越浪费。
/// </summary>
public sealed partial class StatsPage : Page
{
    private enum Range { Month, Year, All }

    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private Range _selectedRange = Range.All; // 精简版之前一直显示全部时间，默认保持这个不变，不让人觉得数字突然变小了

    public StatsPage()
    {
        this.InitializeComponent();
        this.NavigationCacheMode = NavigationCacheMode.Required;

#if __ANDROID__
        _refreshTimer.Tick += (_, _) => RefreshListeningStats();
        _refreshTimer.Start();
        RefreshListeningStats();
#else
        TxtListeningSummary.Text = "听歌统计：这个功能只在 Android 上有意义";
#endif
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    // ── 时间范围切换 ──────────────────────────────────────────────────────────

    private void BtnRangeMonth_Click(object sender, RoutedEventArgs e) => SetRange(Range.Month);

    private void BtnRangeYear_Click(object sender, RoutedEventArgs e) => SetRange(Range.Year);

    private void BtnRangeAll_Click(object sender, RoutedEventArgs e) => SetRange(Range.All);

    private void SetRange(Range range)
    {
        _selectedRange = range;
        UpdateRangeButtonHighlight();
        RefreshListeningStats();
    }

    private void UpdateRangeButtonHighlight()
    {
        var selected = new SolidColorBrush(Color.FromArgb(255, 0x8A, 0xB4, 0xF8));
        var normal = new SolidColorBrush(Color.FromArgb(255, 0x33, 0x33, 0x33));
        BtnRangeMonth.Background = _selectedRange == Range.Month ? selected : normal;
        BtnRangeYear.Background = _selectedRange == Range.Year ? selected : normal;
        BtnRangeAll.Background = _selectedRange == Range.All ? selected : normal;
    }

    private (DateOnly From, DateOnly To, string Label) GetSelectedRange()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        return _selectedRange switch
        {
            Range.Month => (new DateOnly(today.Year, today.Month, 1), today, "本月"),
            Range.Year => (new DateOnly(today.Year, 1, 1), today, "今年"),
            _ => (DateOnly.MinValue, DateOnly.MaxValue, "全部时间"),
        };
    }

#if __ANDROID__
    private static string GetStatsFilePath() =>
        System.IO.Path.Combine(global::Android.App.Application.Context.FilesDir!.AbsolutePath, "stats.json");
#endif

    private void RefreshListeningStats()
    {
#if __ANDROID__
        UpdateRangeButtonHighlight(); // 定时器每 3 秒都会跑到这——顺手保证按钮高亮跟 _selectedRange 一直对得上，不用额外找地方调用一次
        var stats = new ListeningStatsFileStore(GetStatsFilePath()).Load();
        var (from, to, label) = GetSelectedRange();
        var summary = ListeningStatsAggregator.BuildSummary(stats, from, to);

        TxtListeningSummary.Text = summary.TotalSeconds > 0
            ? $"{label}：总时长 {ListeningStatsAggregator.FormatDuration(summary.TotalSeconds)} · 活跃 {summary.ActiveDayCount} 天 · 最长连续 {summary.LongestStreakDays} 天"
            : $"{label}：还没有数据（悬浮窗打开、真的在放歌的时候才会开始攒）";

        BuildTopList(TopArtistsPanel, summary.TopArtists.Select(a => (a.Artist, a.Seconds)), "还没有听够，排不出榜单");
        BuildTopList(TopTracksPanel, summary.TopTracks.Select(t => (string.IsNullOrEmpty(t.Artist) ? t.Title : $"{t.Title} · {t.Artist}", t.Seconds)), "还没有听够，排不出榜单");

        BuildHeatmap(stats);
        BuildAchievementRows(stats);
#endif
    }

#if __ANDROID__
    private static void BuildTopList(StackPanel panel, IEnumerable<(string Label, int Seconds)> entries, string emptyText)
    {
        panel.Children.Clear();
        var list = entries.ToList();
        if (list.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = emptyText,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0x66, 0x66, 0x66)),
            });
            return;
        }

        for (int i = 0; i < list.Count; i++)
        {
            var (label, seconds) = list[i];
            panel.Children.Add(new TextBlock
            {
                Text = $"{i + 1}. {label} · {ListeningStatsAggregator.FormatDuration(seconds)}",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0xFF, 0xFF, 0xFF)),
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    // 热力图格子大小——够小才能塞下一整年 52~53 周还不用横向滚太远，够大又能看清颜色深浅，
    // 跟桌面版那种"格子小小一片"的观感是一个思路，只是这边是真机触屏，没必要做得跟桌面版像素级一样小
    private const double HeatmapCellSize = 16;

    // GitHub 贡献图那种绿色深浅梯度——0 档（完全没听）用比页面背景稍亮一点的灰，不是纯黑，
    // 不然会跟外面留白区分不开，看不出"这是一个格子"
    private static readonly Color[] HeatmapColors =
    {
        Color.FromArgb(255, 0x22, 0x22, 0x22),
        Color.FromArgb(255, 0x0E, 0x44, 0x29),
        Color.FromArgb(255, 0x00, 0x6D, 0x32),
        Color.FromArgb(255, 0x26, 0xA6, 0x41),
        Color.FromArgb(255, 0x39, 0xD3, 0x53),
    };

    private void BuildHeatmap(ListeningStats stats)
    {
        HeatmapGrid.RowDefinitions.Clear();
        HeatmapGrid.ColumnDefinitions.Clear();
        HeatmapGrid.Children.Clear();

        // 固定今年 1 月 1 日到今天——不跟着上面三个时间范围按钮走，见类顶部注释
        var today = DateOnly.FromDateTime(DateTime.Now);
        var jan1 = new DateOnly(today.Year, 1, 1);
        var cells = ListeningHeatmap.BuildCells(stats, jan1, today);
        if (cells.Count == 0)
        {
            TxtHeatmapHint.Text = "";
            return;
        }

        int maxWeek = cells.Max(c => c.Week);
        for (int i = 0; i <= maxWeek; i++) HeatmapGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HeatmapCellSize) });
        for (int i = 0; i < 7; i++) HeatmapGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeatmapCellSize) });

        foreach (var cell in cells)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(HeatmapColors[cell.Level]),
                Margin = new Thickness(1),
                CornerRadius = new CornerRadius(2),
            };
            Grid.SetRow(border, cell.DayOfWeek);
            Grid.SetColumn(border, cell.Week);
            HeatmapGrid.Children.Add(border);
        }

        TxtHeatmapHint.Text = "颜色越亮代表当天听得越久（固定档位，不是跟今年最高的一天比例算的）。";
    }

    private void BuildAchievementRows(ListeningStats stats)
    {
        AchievementsPanel.Children.Clear();
        foreach (var progress in AchievementCalculator.Evaluate(stats))
        {
            var row = new TextBlock
            {
                Text = $"{(progress.Unlocked ? "✅" : "🔒")} {progress.Achievement.Icon} {progress.Achievement.Name}"
                    + $" —— {progress.Achievement.Description}",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = new SolidColorBrush(progress.Unlocked ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 0x55, 0x55, 0x55)),
            };
            AchievementsPanel.Children.Add(row);
        }
    }

    private void BtnHighlights_Click(object sender, RoutedEventArgs e)
    {
        var stats = new ListeningStatsFileStore(GetStatsFilePath()).Load();
        var (from, to, label) = GetSelectedRange();
        var summary = ListeningStatsAggregator.BuildSummary(stats, from, to);
        var slides = ListeningHighlightsBuilder.BuildSlides(summary, label, userName: null); // 手机这边没有桌面版 WelcomeWindow 那个昵称功能，不传名字，标题走 BuildSlides 自己的兜底文案
        Frame.Navigate(typeof(HighlightsPage), slides);
    }
#else
    private void BtnHighlights_Click(object sender, RoutedEventArgs e) { }
#endif
}
