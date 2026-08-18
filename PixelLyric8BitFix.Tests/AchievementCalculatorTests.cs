using System;
using System.Collections.Generic;
using System.Linq;
using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    public class AchievementCalculatorTests
    {
        // 构造 N 天连续听歌的数据，每天随便存 60 秒、随便一个 trackId——这几个测试只关心"连续天数"这一件事
        private static ListeningStats BuildConsecutiveDays(DateOnly start, int count)
        {
            var stats = new ListeningStats();
            stats.Tracks["歌_艺人"] = new TrackInfo { Title = "歌", Artist = "艺人" };
            for (int i = 0; i < count; i++)
            {
                string key = start.AddDays(i).ToString("yyyy-MM-dd");
                stats.Days[key] = new DayStats { TotalSeconds = 60, TrackSeconds = new Dictionary<string, int> { ["歌_艺人"] = 60 } };
            }
            return stats;
        }

        [Fact]
        public void GetLongestStreakDays_EmptyStats_ReturnsZero()
        {
            Assert.Equal(0, PixelLyric8BitFix.ListeningStatsAggregator.GetLongestStreakDays(new ListeningStats()));
        }

        [Fact]
        public void GetLongestStreakDays_AllConsecutive_ReturnsFullCount()
        {
            var stats = BuildConsecutiveDays(new DateOnly(2026, 1, 1), 7);
            Assert.Equal(7, ListeningStatsAggregator.GetLongestStreakDays(stats));
        }

        [Fact]
        public void GetLongestStreakDays_WithGap_ReturnsLongerRunOnly()
        {
            var stats = new ListeningStats();
            stats.Tracks["歌_艺人"] = new TrackInfo { Title = "歌", Artist = "艺人" };
            // 1/1~1/3 连续三天，跳过 1/4，1/5~1/9 连续五天——最长应该是后面这段 5 天
            foreach (var day in new[] { 1, 2, 3, 5, 6, 7, 8, 9 })
            {
                string key = new DateOnly(2026, 1, day).ToString("yyyy-MM-dd");
                stats.Days[key] = new DayStats { TotalSeconds = 60, TrackSeconds = new Dictionary<string, int> { ["歌_艺人"] = 60 } };
            }
            Assert.Equal(5, ListeningStatsAggregator.GetLongestStreakDays(stats));
        }

        [Fact]
        public void GetLongestStreakDays_IgnoresZeroSecondDays()
        {
            var stats = new ListeningStats();
            stats.Days["2026-01-01"] = new DayStats { TotalSeconds = 60 };
            stats.Days["2026-01-02"] = new DayStats { TotalSeconds = 0 }; // 记录存在但没听——不该算进连续天数
            stats.Days["2026-01-03"] = new DayStats { TotalSeconds = 60 };
            Assert.Equal(1, ListeningStatsAggregator.GetLongestStreakDays(stats));
        }

        [Fact]
        public void GetUniqueArtistCount_OnlyCountsTracksWithActualListenSeconds()
        {
            var stats = new ListeningStats();
            stats.Tracks["歌A_艺人X"] = new TrackInfo { Title = "歌A", Artist = "艺人X" };
            stats.Tracks["歌B_艺人Y"] = new TrackInfo { Title = "歌B", Artist = "艺人Y" }; // 注册过但从没真的攒到秒数（切太快跳走了）
            stats.Days["2026-01-01"] = new DayStats
            {
                TotalSeconds = 60,
                TrackSeconds = new Dictionary<string, int> { ["歌A_艺人X"] = 60 },
            };

            Assert.Equal(1, ListeningStatsAggregator.GetUniqueArtistCount(stats));
            Assert.Equal(1, ListeningStatsAggregator.GetUniqueTrackCount(stats));
        }

        [Fact]
        public void Evaluate_NoData_AllLocked()
        {
            var results = AchievementCalculator.Evaluate(new ListeningStats());
            Assert.Equal(8, results.Count); // 7 常规 + 1 压轴
            Assert.All(results, r => Assert.False(r.Unlocked));
        }

        [Fact]
        public void Evaluate_OneHourListened_UnlocksOnlyFirstHour()
        {
            var stats = new ListeningStats();
            stats.Days["2026-01-01"] = new DayStats { TotalSeconds = 3600 };

            var results = AchievementCalculator.Evaluate(stats);
            var firstHour = results.Single(r => r.Achievement.Id == AchievementCalculator.FirstHour.Id);
            var tenHours = results.Single(r => r.Achievement.Id == AchievementCalculator.TenHours.Id);
            var crown = results.Single(r => r.Achievement.Id == AchievementCalculator.CrownSkin.Id);

            Assert.True(firstHour.Unlocked);
            Assert.False(tenHours.Unlocked);
            Assert.False(crown.Unlocked); // 压轴还差得远
        }

        [Fact]
        public void IsCrownSkinUnlocked_RequiresAllSevenRegularAchievements()
        {
            // 手写一份"刚好卡在压轴门槛上"的数据：100 小时、30 天连续、10 个艺人、30 首歌——
            // 100 小时和 30 天连续本身就互相蕴含了 1 小时/10 小时/一周陪伴这几个更低门槛的成就，
            // 不用再单独凑
            var stats = BuildConsecutiveDays(new DateOnly(2026, 1, 1), 30);
            for (int i = 0; i < 30; i++)
            {
                string key = new DateOnly(2026, 1, 1).AddDays(i).ToString("yyyy-MM-dd");
                string trackId = $"歌{i}_艺人{i % 10}"; // 30 首不同歌曲，循环 10 个不同艺人
                stats.Tracks[trackId] = new TrackInfo { Title = $"歌{i}", Artist = $"艺人{i % 10}" };
                stats.Days[key] = new DayStats
                {
                    TotalSeconds = 12000, // 30 天 * 12000 秒 = 100 小时整
                    TrackSeconds = new Dictionary<string, int> { [trackId] = 12000 },
                };
            }

            Assert.True(AchievementCalculator.IsCrownSkinUnlocked(stats));
            Assert.Equal(0, AchievementCalculator.CountRemainingForCrown(stats));
        }

        [Fact]
        public void IsCrownSkinUnlocked_MissingOneAchievement_StaysLocked()
        {
            // 时长/连续天数都达标，但只听过 3 位艺人（差 10 位艺人这个成就）
            var stats = new ListeningStats();
            for (int i = 0; i < 30; i++)
            {
                string key = new DateOnly(2026, 1, 1).AddDays(i).ToString("yyyy-MM-dd");
                string trackId = $"歌{i}_艺人{i % 3}"; // 只有 3 个不同艺人
                stats.Tracks[trackId] = new TrackInfo { Title = $"歌{i}", Artist = $"艺人{i % 3}" };
                stats.Days[key] = new DayStats
                {
                    TotalSeconds = 12000,
                    TrackSeconds = new Dictionary<string, int> { [trackId] = 12000 },
                };
            }

            Assert.False(AchievementCalculator.IsCrownSkinUnlocked(stats));
            Assert.Equal(1, AchievementCalculator.CountRemainingForCrown(stats));
        }
    }
}
