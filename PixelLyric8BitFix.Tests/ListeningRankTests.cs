using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    /// <summary>首页/统计页称号背后的 ListeningRank——档位查找、进度条百分比。</summary>
    public class ListeningRankTests
    {
        [Theory]
        [InlineData(0, "初来乍到")]
        [InlineData(3599, "初来乍到")]
        [InlineData(3600, "渐入佳境")]
        [InlineData(35_999, "渐入佳境")]
        [InlineData(36_000, "老朋友")]
        [InlineData(179_999, "老朋友")]
        [InlineData(180_000, "深度乐迷")]
        [InlineData(360_000, "百炼成钢")]
        [InlineData(1_080_000, "资深发烧友")]
        [InlineData(3_600_000, "音乐之魂")]
        [InlineData(99_999_999, "音乐之魂")] // 封顶之后不管多高都还是最高档，不会越界抛异常
        public void GetCurrentTier_ReturnsExpectedTierName(int totalSeconds, string expectedName)
        {
            Assert.Equal(expectedName, ListeningRank.GetCurrentTier(totalSeconds).Name);
        }

        [Fact]
        public void GetNextTier_AtLowestTier_ReturnsSecondTier()
        {
            var next = ListeningRank.GetNextTier(0);
            Assert.NotNull(next);
            Assert.Equal("渐入佳境", next!.Name);
        }

        [Fact]
        public void GetNextTier_AtHighestTier_ReturnsNull()
        {
            Assert.Null(ListeningRank.GetNextTier(3_600_000));
            Assert.Null(ListeningRank.GetNextTier(99_999_999));
        }

        [Fact]
        public void GetProgressWithinTier_AtTierStart_IsZero()
        {
            Assert.Equal(0.0, ListeningRank.GetProgressWithinTier(3600));
        }

        [Fact]
        public void GetProgressWithinTier_HalfwayToNextTier_IsAboutHalf()
        {
            // 1 小时(3600) -> 10 小时(36000)，中点在 19800
            double progress = ListeningRank.GetProgressWithinTier(19_800);
            Assert.InRange(progress, 0.49, 0.51);
        }

        [Fact]
        public void GetProgressWithinTier_AtHighestTier_IsOne()
        {
            Assert.Equal(1.0, ListeningRank.GetProgressWithinTier(3_600_000));
            Assert.Equal(1.0, ListeningRank.GetProgressWithinTier(99_999_999));
        }

        [Fact]
        public void GetProgressWithinTier_NeverExceedsRange()
        {
            for (int seconds = 0; seconds <= 4_000_000; seconds += 37_777)
            {
                double progress = ListeningRank.GetProgressWithinTier(seconds);
                Assert.InRange(progress, 0.0, 1.0);
            }
        }
    }
}
