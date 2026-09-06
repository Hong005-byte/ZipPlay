using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace PixelLyric8Bit.Mobile;

/// <summary>
/// 听歌统计 + 成就墙——精简版：只挑最核心的几个数字（总时长/活跃天数/连续天数）+ 一份成就
/// checklist，不是桌面版 ListeningStatsWindow 那套本月/今年/全部时间切换 + 热力图 + 亮点回顾。
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
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(3) };

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

#if __ANDROID__
    private static string GetStatsFilePath() =>
        System.IO.Path.Combine(global::Android.App.Application.Context.FilesDir!.AbsolutePath, "stats.json");
#endif

    private void RefreshListeningStats()
    {
#if __ANDROID__
        var stats = new PixelLyric8BitFix.ListeningStatsFileStore(GetStatsFilePath()).Load();

        int totalSeconds = PixelLyric8BitFix.ListeningStatsAggregator.GetTotalSeconds(stats, DateOnly.MinValue, DateOnly.MaxValue);
        int activeDays = PixelLyric8BitFix.ListeningStatsAggregator.GetActiveDayCount(stats, DateOnly.MinValue, DateOnly.MaxValue);
        int longestStreak = PixelLyric8BitFix.ListeningStatsAggregator.GetLongestStreakDays(stats);

        TxtListeningSummary.Text = totalSeconds > 0
            ? $"听歌统计：总时长 {PixelLyric8BitFix.ListeningStatsAggregator.FormatDuration(totalSeconds)} · 活跃 {activeDays} 天 · 最长连续 {longestStreak} 天"
            : "听歌统计：还没有数据（悬浮窗打开、真的在放歌的时候才会开始攒）";

        BuildAchievementRows(stats);
#endif
    }

#if __ANDROID__
    private void BuildAchievementRows(PixelLyric8BitFix.ListeningStats stats)
    {
        AchievementsPanel.Children.Clear();
        foreach (var progress in PixelLyric8BitFix.AchievementCalculator.Evaluate(stats))
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
#endif
}
