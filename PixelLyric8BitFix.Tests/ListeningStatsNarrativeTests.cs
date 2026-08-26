using System;
using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    public class ListeningStatsNarrativeTests
    {
        [Fact]
        public void BuildHeadline_AllFieldsZero_ReturnsNull()
        {
            // 数据太单薄（比如刚开始用、什么记录都还没攒出来）不该硬凑一句话
            var summary = new ListeningSummary();
            Assert.Null(ListeningStatsNarrative.BuildHeadline(summary));
        }

        [Fact]
        public void BuildHeadline_OnlyOneDayStreak_DoesNotMentionStreak()
        {
            // 连续 1 天不算"连续"，不该出现"最长一口气连续听了 1 天"这种没有信息量的话
            var summary = new ListeningSummary { LongestStreakDays = 1, UniqueArtistCount = 3, UniqueTrackCount = 5 };
            string? result = ListeningStatsNarrative.BuildHeadline(summary);
            Assert.NotNull(result);
            Assert.DoesNotContain("连续", result);
            Assert.Contains("3 位不同的艺人", result);
        }

        [Fact]
        public void BuildHeadline_WithStreakAndVariety_CombinesIntoOneSentence()
        {
            var summary = new ListeningSummary { LongestStreakDays = 5, UniqueArtistCount = 10, UniqueTrackCount = 20 };
            string? result = ListeningStatsNarrative.BuildHeadline(summary);
            Assert.Contains("最长一口气连续听了 5 天", result);
            Assert.Contains("认识了 10 位不同的艺人、听过 20 首不同的歌", result);
        }

        [Fact]
        public void BuildHeadline_WithBestDay_AppendsSecondSentence()
        {
            var summary = new ListeningSummary
            {
                LongestStreakDays = 5,
                UniqueArtistCount = 10,
                UniqueTrackCount = 20,
                BestDay = new DateOnly(2026, 3, 15),
                BestDaySeconds = 3600,
            };
            string? result = ListeningStatsNarrative.BuildHeadline(summary);
            Assert.Contains("投入最多的一天是 3月15日", result);
            Assert.Contains("听了 1 小时 0 分钟", result);
        }

        [Fact]
        public void BuildHeadline_OnlyBestDay_NoStreakOrVarietyClause_StillReturnsSentence()
        {
            var summary = new ListeningSummary { BestDay = new DateOnly(2026, 3, 15), BestDaySeconds = 600 };
            string? result = ListeningStatsNarrative.BuildHeadline(summary);
            Assert.NotNull(result);
            Assert.StartsWith("投入最多的一天是", result);
        }

        [Fact]
        public void BuildHeadline_BestDayWithZeroSeconds_IsIgnored()
        {
            // BestDay 有值但秒数是 0——理论上不该出现（GetBestDay 只在 seconds > 之前的最佳值时才更新 bestDate），
            // 但防御性地确认一下：秒数 0 的话不该说"投入最多的一天"
            var summary = new ListeningSummary { BestDay = new DateOnly(2026, 3, 15), BestDaySeconds = 0 };
            Assert.Null(ListeningStatsNarrative.BuildHeadline(summary));
        }
    }
}
