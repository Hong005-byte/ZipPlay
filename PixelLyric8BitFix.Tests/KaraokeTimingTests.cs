using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    public class KaraokeTimingTests
    {
        [Fact]
        public void EstimateSungChars_BeforeLineStarts_ReturnsZero()
        {
            Assert.Equal(0, KaraokeTiming.EstimateSungChars("HELLO", currentMs: 900, lineStartMs: 1000, lineEndMs: 5000));
        }

        [Fact]
        public void EstimateSungChars_AfterLineFullySung_ReturnsFullLength()
        {
            string line = "HELLO"; // 5 个拉丁字符 * 130ms = 650ms 估算唱完时间
            Assert.Equal(line.Length, KaraokeTiming.EstimateSungChars(line, currentMs: 999_999, lineStartMs: 0, lineEndMs: 100_000));
        }

        [Fact]
        public void EstimateSungChars_NormalInterval_LinearlyFillsAcrossWholeInterval()
        {
            // 区间没有明显超时（不到 1.8x 估算时长），直接线性铺开整个区间，走到区间正中点应该唱到一半
            string line = "ABCDEFGHIJ"; // 10 个字符，纯拉丁基准 130ms/char = 1300ms 估算，区间 1500ms 没有超过 1.8x
            int lineStartMs = 0, lineEndMs = 1500;
            int sungAtMidpoint = KaraokeTiming.EstimateSungChars(line, currentMs: 750, lineStartMs, lineEndMs);

            Assert.Equal(5, sungAtMidpoint);
        }

        [Fact]
        public void EstimateFillDurationMs_LongIntervalWithoutOvertime_UsesFullInterval()
        {
            // 区间 1500ms，估算唱完只要 1300ms，没有达到 1.8x 阈值（= 2340ms），不该压缩，直接用整个区间
            int duration = KaraokeTiming.EstimateFillDurationMs("ABCDEFGHIJ", lineStartMs: 0, lineEndMs: 1500);
            Assert.Equal(1500, duration);
        }

        [Fact]
        public void EstimateFillDurationMs_LongInterlude_CompressesToEstimatedSingTime()
        {
            // 区间长达 10 秒（明显带了一大段前奏/间奏），但这行字很短，估算唱完时间远小于区间的 1.8 倍，
            // 应该压缩成估算值，而不是让扫光在间奏里"傻等"
            string line = "HI"; // 2 个拉丁字符 * 130ms = 260ms，Math.Max(300, ...) 兜底成 300ms
            int duration = KaraokeTiming.EstimateFillDurationMs(line, lineStartMs: 0, lineEndMs: 10_000);
            Assert.Equal(300, duration);
        }

        [Fact]
        public void EstimateFillDurationMs_PureChineseLine_TakesLongerPerCharThanLatin()
        {
            // 同样字数，纯中文行估算出来的"唱完时间"应该明显比纯英文行长——中文一个字一个完整音节
            string latin = "ABCDE";
            string chinese = "中文歌词句"; // 5 个 CJK 字符

            int latinDuration = KaraokeTiming.EstimateFillDurationMs(latin, lineStartMs: 0, lineEndMs: 100_000);
            int chineseDuration = KaraokeTiming.EstimateFillDurationMs(chinese, lineStartMs: 0, lineEndMs: 100_000);

            Assert.True(chineseDuration > latinDuration,
                $"中文行估算时长应该更长，实际 中文={chineseDuration}ms 英文={latinDuration}ms");
        }

        [Fact]
        public void EstimateFillDurationMs_MixedLanguage_FallsBetweenPureLatinAndPureChinese()
        {
            string latin = "ABCDEABCDE";     // 10 拉丁字符
            string chinese = "中文歌词句子很长哦"; // 10 CJK 字符（凑够跟上面等长）
            string mixed = "AB中文CD歌词";        // 10 字符，6 拉丁 + 4 CJK

            int latinDuration = KaraokeTiming.EstimateFillDurationMs(latin, 0, 100_000);
            int chineseDuration = KaraokeTiming.EstimateFillDurationMs(chinese, 0, 100_000);
            int mixedDuration = KaraokeTiming.EstimateFillDurationMs(mixed, 0, 100_000);

            Assert.InRange(mixedDuration, latinDuration, chineseDuration);
        }

        [Fact]
        public void EstimateSungChars_EmptyLine_ReturnsZero()
        {
            Assert.Equal(0, KaraokeTiming.EstimateSungChars("", currentMs: 5000, lineStartMs: 0, lineEndMs: 10_000));
        }
    }
}
