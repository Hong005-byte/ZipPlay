using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    public class MobileAchievementUnlockTrackerTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "zipplay_unlock_tests_" + Guid.NewGuid().ToString("N"));
        private string FilePath => Path.Combine(_dir, "seen.json");

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
        }

        private static AchievementProgress Progress(string id, bool unlocked) => new()
        {
            Achievement = new AchievementDefinition { Id = id, Icon = "🎧", Name = id, Description = "" },
            Unlocked = unlocked,
        };

        [Fact]
        public void FirstRun_AlreadyUnlockedAchievements_DoNotCelebrate()
        {
            // 第一次跑（本地压根没有这份记录，比如刚更新到带这个功能的版本、但早就攒了好几个成就）
            // 不该把已经解锁过的也当成"刚解锁"弹一串庆祝
            var tracker = new MobileAchievementUnlockTracker(FilePath);

            var newly = tracker.DetectNewlyUnlocked(new[] { Progress("a", true), Progress("b", false) });

            Assert.Empty(newly);
        }

        [Fact]
        public void SecondRun_NewlyUnlockedAchievement_IsReported()
        {
            var tracker = new MobileAchievementUnlockTracker(FilePath);
            tracker.DetectNewlyUnlocked(new[] { Progress("a", true), Progress("b", false) }); // 建基线

            var newly = tracker.DetectNewlyUnlocked(new[] { Progress("a", true), Progress("b", true) });

            Assert.Single(newly);
            Assert.Equal("b", newly[0].Id);
        }

        [Fact]
        public void SameStateAgain_ReportsNothing()
        {
            var tracker = new MobileAchievementUnlockTracker(FilePath);
            tracker.DetectNewlyUnlocked(new[] { Progress("a", true) });

            var newly = tracker.DetectNewlyUnlocked(new[] { Progress("a", true) });

            Assert.Empty(newly);
        }

        [Fact]
        public void MultipleNewUnlocksAtOnce_AllReported()
        {
            var tracker = new MobileAchievementUnlockTracker(FilePath);
            // 先建一次真的落了盘的基线（带一个已解锁的，不然 a/b 全是 false，这次调用不会写文件，
            // 下一次调用仍然会被当成"第一次跑"，见 FirstRun_AlreadyUnlockedAchievements_DoNotCelebrate）
            tracker.DetectNewlyUnlocked(new[] { Progress("baseline", true), Progress("a", false), Progress("b", false) });

            var newly = tracker.DetectNewlyUnlocked(new[] { Progress("baseline", true), Progress("a", true), Progress("b", true) });

            Assert.Equal(2, newly.Count);
        }
    }
}
