using System.Collections.Generic;
using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    public class MiniAchievementRingTests
    {
        private static AchievementDefinition Def(string id) => new() { Id = id, Icon = "x", Name = id, Description = id };

        private static List<AchievementProgress> BuildProgress(params bool[] unlocked)
        {
            var list = new List<AchievementProgress>();
            for (int i = 0; i < unlocked.Length; i++)
            {
                list.Add(new AchievementProgress { Achievement = Def($"a{i}"), Unlocked = unlocked[i] });
            }
            return list;
        }

        [Fact]
        public void BuildDots_EmptyProgress_ReturnsEmptyList()
        {
            Assert.Empty(MiniAchievementRing.BuildDots(new List<AchievementProgress>(), 70, 65));
        }

        [Fact]
        public void BuildDots_ReturnsOneDotPerAchievement()
        {
            var progress = BuildProgress(true, false, true, false, true, false, true, true); // 8 个（7 常规 + 1 压轴）
            var dots = MiniAchievementRing.BuildDots(progress, 70, 65);
            Assert.Equal(8, dots.Count);
        }

        [Fact]
        public void BuildDots_UnlockedRegular_UsesFixedRegularColor_NotSkinAccent()
        {
            // 故意不吃调用方传的皮肤强调色——跟音频粒子环（用皮肤强调色）刻意用不同颜色，
            // 不然两圈会糊在一起分不清哪颗是成就哪颗是律动（离屏截图实测过这个问题，见 MiniAchievementRing 注释）
            var progress = BuildProgress(true, false); // 第 0 个是"常规"（不是最后一个），第 1 个是压轴
            var dots = MiniAchievementRing.BuildDots(progress, 70, 65);

            Assert.Equal(MiniAchievementRing.UnlockedRegularColor, dots[0].Color);
            Assert.True(dots[0].Opacity > 0.5 && dots[0].Opacity < 1.0);
        }

        [Fact]
        public void BuildDots_LockedRegular_UsesLowOpacity()
        {
            var progress = BuildProgress(false, true);
            var dots = MiniAchievementRing.BuildDots(progress, 70, 65);
            Assert.True(dots[0].Opacity < 0.2); // 没解锁——暗点，不是完全不显示（Opacity 不是 0）
            Assert.True(dots[0].Opacity > 0);
        }

        [Fact]
        public void BuildDots_UnlockedCapstone_UsesGoldColor_NotRegularColor()
        {
            var progress = BuildProgress(true, true); // 最后一个（索引 1）是压轴
            var dots = MiniAchievementRing.BuildDots(progress, 70, 65);

            Assert.Equal(MiniAchievementRing.CapstoneGoldColor, dots[1].Color);
            Assert.NotEqual(MiniAchievementRing.UnlockedRegularColor, dots[1].Color);
        }

        [Fact]
        public void BuildDots_LockedCapstone_UsesRegularColor_NotGold()
        {
            // 压轴成就本身没解锁的话，不该提前显示金色——金色是"已经拿到"的奖励感，不是坑位标记
            var progress = BuildProgress(true, false); // 索引 1（压轴）没解锁
            var dots = MiniAchievementRing.BuildDots(progress, 70, 65);

            Assert.Equal(MiniAchievementRing.UnlockedRegularColor, dots[1].Color);
            Assert.NotEqual(MiniAchievementRing.CapstoneGoldColor, dots[1].Color);
        }

        [Fact]
        public void BuildDots_AllUnlocked_EveryDotHasFullyUnlockedOpacity()
        {
            var progress = BuildProgress(true, true, true, true, true, true, true, true);
            var dots = MiniAchievementRing.BuildDots(progress, 70, 65);
            Assert.All(dots, d => Assert.True(d.Opacity >= 0.6));
        }

        [Fact]
        public void BuildDots_FirstDotIsAtTwelveOClock()
        {
            // 第一个点从 12 点钟方向开始（正上方，也就是 x = center，y = center - radius）
            double center = 70, radius = 65;
            var progress = BuildProgress(true);
            var dots = MiniAchievementRing.BuildDots(progress, center, radius);

            Assert.Equal(center, dots[0].CenterX, precision: 5);
            Assert.Equal(center - radius, dots[0].CenterY, precision: 5);
        }

        [Fact]
        public void BuildDots_AllDotsAreExactlyRadiusFromCenter()
        {
            double center = 70, radius = 65;
            var progress = BuildProgress(true, false, true, false, true, false, true, true);
            var dots = MiniAchievementRing.BuildDots(progress, center, radius);

            foreach (var dot in dots)
            {
                double dx = dot.CenterX - center;
                double dy = dot.CenterY - center;
                double distance = System.Math.Sqrt(dx * dx + dy * dy);
                Assert.Equal(radius, distance, precision: 5);
            }
        }
    }
}
