using System;
using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    /// <summary>首页问候语文案池——用固定 seed 的 Random 让结果可预测，不是真的测"随机对不对"，
    /// 是测"没填名字走引导文案""填了名字的话，不管抽到哪句，格式/称呼都对"这些不该出错的边界。</summary>
    public class HomeGreetingBuilderTests
    {
        [Fact]
        public void Build_NoUserName_AlwaysReturnsOnboardingPrompt()
        {
            // 没填名字的时候不该套用时间段/数据文案池——不管随机数抽到什么，都得是引导文案
            for (int seed = 0; seed < 20; seed++)
            {
                string result = HomeGreetingBuilder.Build(new DateTime(2026, 1, 1, 9, 0, 0), null, new ListeningStats(), new Random(seed));
                Assert.Equal("设置你的名字/头像 →", result);
            }
        }

        [Fact]
        public void Build_EmptyOrWhitespaceUserName_AlsoReturnsOnboardingPrompt()
        {
            string result = HomeGreetingBuilder.Build(new DateTime(2026, 1, 1, 9, 0, 0), "   ", new ListeningStats(), new Random(1));
            Assert.Equal("设置你的名字/头像 →", result);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(4)]
        [InlineData(8)]
        [InlineData(12)]
        [InlineData(15)]
        [InlineData(20)]
        [InlineData(23)]
        public void Build_WithUserName_AlwaysContainsNameAndSuffix(int hour)
        {
            // 空统计数据，数据相关文案不会被抽到（TryBuildStatsGreeting 返回 null），
            // 一定落回时间段文案池——不管几点，结果里都得包含用户名和固定后缀
            var now = new DateTime(2026, 1, 1, hour, 30, 0);
            for (int seed = 0; seed < 10; seed++)
            {
                string result = HomeGreetingBuilder.Build(now, "小明", new ListeningStats(), new Random(seed));
                Assert.Contains("小明", result);
                Assert.Contains("👋（点这里改资料）", result);
            }
        }

        [Fact]
        public void Build_WithStreakData_CanProduceStatsAwareGreeting()
        {
            // 连续 5 天都有记录——够格触发"连续听了 N 天"这句，多试几个 seed 保证至少出现一次
            var stats = new ListeningStats();
            var start = new DateOnly(2026, 1, 1);
            for (int i = 0; i < 5; i++)
            {
                stats.Days[start.AddDays(i).ToString("yyyy-MM-dd")] = new DayStats { TotalSeconds = 600 };
            }

            bool sawStreakLine = false;
            for (int seed = 0; seed < 100; seed++)
            {
                string result = HomeGreetingBuilder.Build(new DateTime(2026, 1, 6, 9, 0, 0), "小明", stats, new Random(seed));
                if (result.Contains("连续听了")) sawStreakLine = true;
            }
            Assert.True(sawStreakLine, "跑了 100 个 seed，一次\"连续听了 N 天\"都没抽到，概率上不太正常");
        }

        [Fact]
        public void Build_WithTodayListening_CanMentionTodaysDuration()
        {
            var now = new DateTime(2026, 3, 15, 20, 0, 0);
            var stats = new ListeningStats();
            stats.Days[DateOnly.FromDateTime(now).ToString("yyyy-MM-dd")] = new DayStats { TotalSeconds = 600 };

            bool sawTodayLine = false;
            for (int seed = 0; seed < 100; seed++)
            {
                if (HomeGreetingBuilder.Build(now, "小明", stats, new Random(seed)).Contains("今天已经听了")) sawTodayLine = true;
            }
            Assert.True(sawTodayLine, "跑了 100 个 seed，一次\"今天已经听了\"都没抽到，概率上不太正常");
        }

        [Fact]
        public void Build_TodayListeningTooShort_DoesNotMentionToday()
        {
            // 门槛是 5 分钟，只听了 1 分钟不该被拿出来说
            var now = new DateTime(2026, 3, 15, 20, 0, 0);
            var stats = new ListeningStats();
            stats.Days[DateOnly.FromDateTime(now).ToString("yyyy-MM-dd")] = new DayStats { TotalSeconds = 60 };

            for (int seed = 0; seed < 50; seed++)
            {
                Assert.DoesNotContain("今天已经听了", HomeGreetingBuilder.Build(now, "小明", stats, new Random(seed)));
            }
        }

        [Fact]
        public void Build_WithTopTrack_CanMentionTrackTitle()
        {
            var stats = new ListeningStats();
            stats.Tracks["晴天_周杰伦"] = new TrackInfo { Title = "晴天", Artist = "周杰伦" };
            stats.Days["2026-01-01"] = new DayStats
            {
                TotalSeconds = 200,
                TrackSeconds = new System.Collections.Generic.Dictionary<string, int> { ["晴天_周杰伦"] = 200 },
            };

            bool sawTrackLine = false;
            for (int seed = 0; seed < 100; seed++)
            {
                if (HomeGreetingBuilder.Build(new DateTime(2026, 1, 2, 9, 0, 0), "小明", stats, new Random(seed)).Contains("《晴天》")) sawTrackLine = true;
            }
            Assert.True(sawTrackLine, "跑了 100 个 seed，一次带歌名的问候都没抽到，概率上不太正常");
        }

        [Fact]
        public void Build_WithUnlockedAchievement_CanMentionAchievementCount()
        {
            // 累计听满 1 小时正好解锁"初次启程"这一个成就
            var stats = new ListeningStats();
            stats.Days["2026-01-01"] = new DayStats { TotalSeconds = 3700 };

            bool sawAchievementLine = false;
            for (int seed = 0; seed < 100; seed++)
            {
                if (HomeGreetingBuilder.Build(new DateTime(2026, 1, 2, 9, 0, 0), "小明", stats, new Random(seed)).Contains("已经解锁")) sawAchievementLine = true;
            }
            Assert.True(sawAchievementLine, "跑了 100 个 seed，一次\"已经解锁\"都没抽到，概率上不太正常");
        }

        [Fact]
        public void Build_NoUnlockedAchievements_NeverMentionsAchievements()
        {
            var stats = new ListeningStats(); // 空数据，一个成就都解不开
            for (int seed = 0; seed < 50; seed++)
            {
                Assert.DoesNotContain("已经解锁", HomeGreetingBuilder.Build(new DateTime(2026, 1, 2, 9, 0, 0), "小明", stats, new Random(seed)));
            }
        }

        [Fact]
        public void Build_With30ActiveDays_CanMentionTotalActiveDays()
        {
            var stats = new ListeningStats();
            for (int i = 0; i < 30; i++)
            {
                stats.Days[new DateOnly(2026, 1, 1).AddDays(i).ToString("yyyy-MM-dd")] = new DayStats { TotalSeconds = 60 };
            }

            bool sawActiveDaysLine = false;
            for (int seed = 0; seed < 100; seed++)
            {
                if (HomeGreetingBuilder.Build(new DateTime(2026, 2, 1, 9, 0, 0), "小明", stats, new Random(seed)).Contains("已经用 ZipPlay 听歌")) sawActiveDaysLine = true;
            }
            Assert.True(sawActiveDaysLine, "跑了 100 个 seed，一次\"已经用 ZipPlay 听歌\"都没抽到，概率上不太正常");
        }
    }
}
