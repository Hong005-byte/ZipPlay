using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    public class ListeningStatsFileStoreTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "zipplay_stats_tests_" + Guid.NewGuid().ToString("N"));
        private string FilePath => Path.Combine(_dir, "stats.json");

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* 测试环境清理失败不影响结果 */ }
        }

        [Fact]
        public void Load_NoFile_ReturnsEmptyStats()
        {
            var store = new ListeningStatsFileStore(FilePath);
            var stats = store.Load();

            Assert.Empty(stats.Days);
            Assert.Empty(stats.Tracks);
        }

        [Fact]
        public void Save_ThenLoad_RoundTrips()
        {
            var store = new ListeningStatsFileStore(FilePath);
            var stats = new ListeningStats();
            stats.Days["2026-09-04"] = new DayStats { TotalSeconds = 120, TrackSeconds = { ["歌|artist"] = 120 } };
            stats.Tracks["歌|artist"] = new TrackInfo { Title = "歌", Artist = "artist" };

            store.Save(stats);
            var loaded = store.Load();

            Assert.Equal(120, loaded.Days["2026-09-04"].TotalSeconds);
            Assert.Equal(120, loaded.Days["2026-09-04"].TrackSeconds["歌|artist"]);
            Assert.Equal("歌", loaded.Tracks["歌|artist"].Title);
        }

        [Fact]
        public void Load_CorruptedFile_ReturnsEmptyStatsWithoutThrowing()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(FilePath, "这不是合法的 JSON {{{");

            var store = new ListeningStatsFileStore(FilePath);
            var stats = store.Load();

            Assert.Empty(stats.Days);
        }

        [Fact]
        public void Load_NullDaysInJson_DoesNotThrowAndFillsDefault()
        {
            // 文件被手动改过、显式写了 "Days": null——反序列化本身不抛异常，但不该让后面用到 Days 的
            // 地方直接 NullReferenceException
            Directory.CreateDirectory(_dir);
            File.WriteAllText(FilePath, """{"Days": null, "Tracks": null}""");

            var store = new ListeningStatsFileStore(FilePath);
            var stats = store.Load();

            Assert.NotNull(stats.Days);
            Assert.NotNull(stats.Tracks);
        }

        [Fact]
        public void Clear_RemovesFile_SubsequentLoadIsEmpty()
        {
            var store = new ListeningStatsFileStore(FilePath);
            var stats = new ListeningStats();
            stats.Days["2026-09-04"] = new DayStats { TotalSeconds = 60 };
            store.Save(stats);

            store.Clear();

            Assert.Empty(store.Load().Days);
        }

        [Fact]
        public void Clear_NoFile_DoesNotThrow()
        {
            var store = new ListeningStatsFileStore(FilePath);
            store.Clear(); // 文件本来就不存在的情况下清空——不该抛异常
        }
    }
}
