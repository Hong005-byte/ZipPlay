using System;
using System.Collections.Generic;
using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    public class ListeningStatsAggregatorTests
    {
        // 构造一份手写的统计数据：两天，"歌A"（艺人X）横跨两天听了 100+50 秒，"歌B"（艺人Y）只在第一天听了 30 秒。
        // 覆盖"同一首歌听好几天要累加""不同艺人要分开排名"这两个最容易写错的地方。
        private static ListeningStats BuildSampleStats()
        {
            var stats = new ListeningStats();
            stats.Tracks["歌A_艺人X"] = new TrackInfo { Title = "歌A", Artist = "艺人X" };
            stats.Tracks["歌B_艺人Y"] = new TrackInfo { Title = "歌B", Artist = "艺人Y" };

            stats.Days["2026-01-01"] = new DayStats
            {
                TotalSeconds = 130,
                TrackSeconds = new Dictionary<string, int> { ["歌A_艺人X"] = 100, ["歌B_艺人Y"] = 30 },
            };
            stats.Days["2026-01-02"] = new DayStats
            {
                TotalSeconds = 50,
                TrackSeconds = new Dictionary<string, int> { ["歌A_艺人X"] = 50 },
            };
            // 第三天有记录但时长是 0（理论上不该出现，但防御一下），不该被算进"活跃天数"
            stats.Days["2026-01-03"] = new DayStats { TotalSeconds = 0 };

            return stats;
        }

        [Fact]
        public void GetTotalSeconds_SumsAcrossDaysInRange()
        {
            var stats = BuildSampleStats();
            int total = ListeningStatsAggregator.GetTotalSeconds(stats, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 3));
            Assert.Equal(180, total); // 130 + 50 + 0
        }

        [Fact]
        public void GetTotalSeconds_ExcludesDaysOutsideRange()
        {
            var stats = BuildSampleStats();
            int total = ListeningStatsAggregator.GetTotalSeconds(stats, new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 3));
            Assert.Equal(50, total);
        }

        [Fact]
        public void GetActiveDayCount_OnlyCountsDaysWithPositiveSeconds()
        {
            var stats = BuildSampleStats();
            int days = ListeningStatsAggregator.GetActiveDayCount(stats, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 3));
            Assert.Equal(2, days); // 1/1 和 1/2 有听，1/3 是 0 秒不算
        }

        [Fact]
        public void GetTopArtists_AggregatesAcrossDaysAndRanksDescending()
        {
            var stats = BuildSampleStats();
            var artists = ListeningStatsAggregator.GetTopArtists(stats, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 3), topN: 5);

            Assert.Equal(2, artists.Count);
            Assert.Equal("艺人X", artists[0].Artist);
            Assert.Equal(150, artists[0].Seconds); // 100 + 50，跨两天累加
            Assert.Equal("艺人Y", artists[1].Artist);
            Assert.Equal(30, artists[1].Seconds);
        }

        [Fact]
        public void GetTopTracks_AggregatesSameTrackAcrossDays()
        {
            var stats = BuildSampleStats();
            var tracks = ListeningStatsAggregator.GetTopTracks(stats, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 3), topN: 5);

            var trackA = Assert.Single(tracks, t => t.Title == "歌A");
            Assert.Equal(150, trackA.Seconds);
            Assert.Equal("艺人X", trackA.Artist);
        }

        [Fact]
        public void GetTopArtists_RespectsTopNLimit()
        {
            var stats = BuildSampleStats();
            var artists = ListeningStatsAggregator.GetTopArtists(stats, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 3), topN: 1);
            Assert.Single(artists);
            Assert.Equal("艺人X", artists[0].Artist);
        }

        [Fact]
        public void BuildSummary_EmptyStats_ReturnsZeroedSummaryNotNull()
        {
            var stats = new ListeningStats();
            var summary = ListeningStatsAggregator.BuildSummary(stats, DateOnly.MinValue, DateOnly.MaxValue);

            Assert.Equal(0, summary.TotalSeconds);
            Assert.Equal(0, summary.ActiveDayCount);
            Assert.Empty(summary.TopArtists);
            Assert.Empty(summary.TopTracks);
        }

        [Fact]
        public void GetTopTracks_MissingTrackInfo_FallsBackToTrackIdInsteadOfCrashing()
        {
            // 防御性场景：day 里出现了一个 Tracks 表里没有登记过的 trackId（理论上不该发生，
            // 但数据文件可能被手动改坏，或者未来某个 bug 漏注册了）——不该直接崩，应该退化成显示 trackId 本身
            var stats = new ListeningStats();
            stats.Days["2026-01-01"] = new DayStats
            {
                TotalSeconds = 10,
                TrackSeconds = new Dictionary<string, int> { ["幽灵歌曲_未知"] = 10 },
            };

            var tracks = ListeningStatsAggregator.GetTopTracks(stats, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), topN: 5);

            var only = Assert.Single(tracks);
            Assert.Equal("幽灵歌曲_未知", only.Title);
            Assert.Equal(10, only.Seconds);
        }

        [Fact]
        public void GetBestDay_ReturnsHighestSingleDayWithinRange()
        {
            var stats = BuildSampleStats(); // 1/1 共 130 秒，1/2 共 50 秒，1/3 共 0 秒
            var (date, seconds) = ListeningStatsAggregator.GetBestDay(stats, DateOnly.MinValue, DateOnly.MaxValue);
            Assert.Equal(new DateOnly(2026, 1, 1), date);
            Assert.Equal(130, seconds);
        }

        [Fact]
        public void GetBestDay_RangeExcludesTheBestDay_FallsBackToNextHighestInRange()
        {
            var stats = BuildSampleStats();
            var (date, seconds) = ListeningStatsAggregator.GetBestDay(stats, new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 3));
            Assert.Equal(new DateOnly(2026, 1, 2), date);
            Assert.Equal(50, seconds);
        }

        [Fact]
        public void GetBestDay_NoRecordsInRange_ReturnsNullDateAndZeroSeconds()
        {
            var (date, seconds) = ListeningStatsAggregator.GetBestDay(new ListeningStats(), DateOnly.MinValue, DateOnly.MaxValue);
            Assert.Null(date);
            Assert.Equal(0, seconds);
        }

        [Fact]
        public void GetLongestStreakDays_RangedOverload_ExcludesDaysOutsideRange()
        {
            var stats = new ListeningStats();
            for (int i = 1; i <= 7; i++)
            {
                stats.Days[new DateOnly(2026, 1, i).ToString("yyyy-MM-dd")] = new DayStats { TotalSeconds = 60 };
            }
            // 全部时间是连续 7 天，但把范围收窄到 1/1~1/3，应该只看得到 3 天
            Assert.Equal(7, ListeningStatsAggregator.GetLongestStreakDays(stats));
            Assert.Equal(3, ListeningStatsAggregator.GetLongestStreakDays(stats, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 3)));
        }

        [Fact]
        public void GetUniqueArtistCount_And_GetUniqueTrackCount_RangedOverload_ExcludesOutsideRange()
        {
            var stats = BuildSampleStats(); // 歌A/艺人X 横跨 1/1~1/2，歌B/艺人Y 只在 1/1
            Assert.Equal(2, ListeningStatsAggregator.GetUniqueArtistCount(stats)); // 全部时间：两位艺人都算
            Assert.Equal(1, ListeningStatsAggregator.GetUniqueArtistCount(stats, new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 2))); // 只看 1/2：只有艺人X
            Assert.Equal(2, ListeningStatsAggregator.GetUniqueTrackCount(stats));
            Assert.Equal(1, ListeningStatsAggregator.GetUniqueTrackCount(stats, new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 2)));
        }

        [Fact]
        public void BuildSummary_PopulatesNarrativeFields()
        {
            var stats = BuildSampleStats();
            var summary = ListeningStatsAggregator.BuildSummary(stats, DateOnly.MinValue, DateOnly.MaxValue);

            Assert.Equal(2, summary.LongestStreakDays); // 1/1、1/2 连续两天（1/3 是 0 秒不算活跃）
            Assert.Equal(2, summary.UniqueArtistCount);
            Assert.Equal(2, summary.UniqueTrackCount);
            Assert.Equal(new DateOnly(2026, 1, 1), summary.BestDay);
            Assert.Equal(130, summary.BestDaySeconds);
        }
    }
}
