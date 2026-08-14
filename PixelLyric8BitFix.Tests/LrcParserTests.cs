using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    public class LrcParserTests
    {
        [Fact]
        public void ParseLines_ReturnsLinesSortedByTime()
        {
            string lrc = "[00:10.50]Second\n[00:05.00]First\n[00:20.00]Third";

            var lines = LrcParser.ParseLines(lrc);

            Assert.Equal(3, lines.Count);
            Assert.Equal(5000, lines[0].TimeMs);
            Assert.Equal("First", lines[0].Text);
            Assert.Equal(10500, lines[1].TimeMs);
            Assert.Equal(20000, lines[2].TimeMs);
        }

        [Theory]
        [InlineData("[00:01.5]Hi", 1500)]   // 1 位小数：补两个 0
        [InlineData("[00:01.50]Hi", 1500)]  // 2 位小数：补一个 0
        [InlineData("[00:01.500]Hi", 1500)] // 3 位小数：原样用
        [InlineData("[01:00.00]Hi", 60000)] // 分钟要乘 60000
        public void ParseLines_NormalizesMillisecondDigitsCorrectly(string lrc, int expectedMs)
        {
            var lines = LrcParser.ParseLines(lrc);

            Assert.Single(lines);
            Assert.Equal(expectedMs, lines[0].TimeMs);
        }

        [Fact]
        public void ParseLines_DuplicateTimestamp_LaterLineWins()
        {
            string lrc = "[00:05.00]Old\n[00:05.00]New";

            var lines = LrcParser.ParseLines(lrc);

            Assert.Single(lines);
            Assert.Equal("New", lines[0].Text);
        }

        [Fact]
        public void ParseLines_SkipsEmptyTextLines()
        {
            // 纯音乐/前奏常见写法：只有时间戳没有歌词，这种行不该占一个"空歌词"条目
            string lrc = "[00:05.00]\n[00:10.00]Real line";

            var lines = LrcParser.ParseLines(lrc);

            Assert.Single(lines);
            Assert.Equal("Real line", lines[0].Text);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("no timestamps here at all")]
        public void ParseLines_InvalidOrEmptyInput_ReturnsEmptyList(string? lrc)
        {
            Assert.Empty(LrcParser.ParseLines(lrc));
        }

        [Fact]
        public void GetLastTimestamp_ReturnsMaxAcrossWholeFile_NotJustLastLine()
        {
            // 时间戳不一定是按文件顺序排的（虽然大多数正常 LRC 是），GetLastTimestamp 应该取全局最大值
            string lrc = "[00:20.00]B\n[00:05.00]A\n[00:15.00]C";

            var last = LrcParser.GetLastTimestamp(lrc);

            Assert.Equal(System.TimeSpan.FromSeconds(20), last);
        }

        [Fact]
        public void GetLastTimestamp_NoTimestamps_ReturnsNull()
        {
            Assert.Null(LrcParser.GetLastTimestamp("just some text"));
        }

        [Fact]
        public void GetLastTimestamp_EmptyInput_ReturnsNull()
        {
            Assert.Null(LrcParser.GetLastTimestamp(""));
            Assert.Null(LrcParser.GetLastTimestamp(null));
        }
    }
}
