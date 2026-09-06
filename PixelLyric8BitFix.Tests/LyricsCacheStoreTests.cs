using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    public class LyricsCacheStoreTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "zipplay_lyrics_cache_tests_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* 测试环境清理失败不影响结果 */ }
        }

        [Fact]
        public void TryGet_NothingSaved_ReturnsNull()
        {
            var store = new LyricsCacheStore(_dir);
            Assert.Null(store.TryGet("怎麼了|周興哲"));
        }

        [Fact]
        public void Save_ThenTryGet_RoundTrips()
        {
            var store = new LyricsCacheStore(_dir);
            store.Save("怎麼了|周興哲", "[00:00.00]失去愛那一刻 才曉得");

            Assert.Equal("[00:00.00]失去愛那一刻 才曉得", store.TryGet("怎麼了|周興哲"));
        }

        [Fact]
        public void Save_EmptyContent_DoesNotWriteFile()
        {
            var store = new LyricsCacheStore(_dir);
            store.Save("track", "");

            Assert.Null(store.TryGet("track"));
            Assert.False(Directory.Exists(_dir)); // 空内容不该建目录/写文件，纯粹的空操作
        }

        [Fact]
        public void Translation_RoundTrips_IndependentlyFromOriginal()
        {
            var store = new LyricsCacheStore(_dir);
            store.Save("track", "原文");
            store.SaveTranslation("track", "translated");

            Assert.Equal("原文", store.TryGet("track"));
            Assert.Equal("translated", store.TryGetTranslation("track"));
        }

        [Fact]
        public void TryGetTranslation_OriginalCachedButNoTranslation_ReturnsNull()
        {
            // 原文缓存命中不代表翻译也一定有——翻译是现场翻的，翻完才顺手存一份，见 LyricsCacheStore 顶部注释
            var store = new LyricsCacheStore(_dir);
            store.Save("track", "原文");

            Assert.Null(store.TryGetTranslation("track"));
        }

        [Fact]
        public void DifferentTrackIds_DoNotCollide()
        {
            var store = new LyricsCacheStore(_dir);
            store.Save("歌A|艺人A", "词A");
            store.Save("歌B|艺人B", "词B");

            Assert.Equal("词A", store.TryGet("歌A|艺人A"));
            Assert.Equal("词B", store.TryGet("歌B|艺人B"));
        }

        [Fact]
        public void GetStats_CountsOnlyOriginalsNotTranslations()
        {
            var store = new LyricsCacheStore(_dir);
            store.Save("track1", "词1");
            store.Save("track2", "词2");
            store.SaveTranslation("track1", "translation1");

            var (count, totalBytes) = store.GetStats();

            Assert.Equal(2, count); // 只算原文份数，翻译是附属不重复计数
            Assert.True(totalBytes > 0);
        }

        [Fact]
        public void GetStats_NothingCached_ReturnsZero()
        {
            var store = new LyricsCacheStore(_dir);
            var (count, totalBytes) = store.GetStats();

            Assert.Equal(0, count);
            Assert.Equal(0, totalBytes);
        }

        [Fact]
        public void Clear_RemovesOriginalAndTranslation()
        {
            var store = new LyricsCacheStore(_dir);
            store.Save("track", "原文");
            store.SaveTranslation("track", "translated");

            store.Clear();

            Assert.Null(store.TryGet("track"));
            Assert.Null(store.TryGetTranslation("track"));
        }

        [Fact]
        public void Clear_EmptyCacheDir_DoesNotThrow()
        {
            var store = new LyricsCacheStore(_dir);
            store.Clear(); // 目录还不存在的情况下清空——不该抛异常
        }
    }
}
