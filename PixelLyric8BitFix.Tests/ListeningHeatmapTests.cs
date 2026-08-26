using System;
using System.Collections.Generic;
using System.Linq;
using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    /// <summary>"🔥 听歌热力图"背后的 ListeningHeatmap——强度档位边界 + 日期摆位（第几周/星期几）
    /// 这两块纯计算逻辑，不碰 UI/磁盘。</summary>
    public class ListeningHeatmapTests
    {
        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 1)]
        [InlineData(14 * 60, 1)] // 14 分钟，还没到 15 分钟那个档
        [InlineData(15 * 60, 2)]
        [InlineData(59 * 60, 2)]
        [InlineData(60 * 60, 3)]
        [InlineData(2 * 3600, 3)]
        [InlineData(3 * 3600, 4)]
        [InlineData(10 * 3600, 4)] // 超过最高档位也还是 4，不会有第 5 档
        public void GetIntensityLevel_MapsSecondsToExpectedLevel(int seconds, int expectedLevel)
        {
            Assert.Equal(expectedLevel, ListeningHeatmap.GetIntensityLevel(seconds));
        }

        [Fact]
        public void BuildCells_CoversEveryDayInRange_IncludingDaysWithNoData()
        {
            var stats = new ListeningStats();
            var from = new DateOnly(2026, 1, 1);
            var to = new DateOnly(2026, 1, 10); // 10 天，其中大多数没有任何记录

            var cells = ListeningHeatmap.BuildCells(stats, from, to);

            Assert.Equal(10, cells.Count);
            Assert.All(cells, c => Assert.Equal(0, c.Level)); // 没有任何 Days 记录，全是 0 档
            Assert.Equal(from, cells[0].Date);
            Assert.Equal(to, cells[^1].Date);
        }

        [Fact]
        public void BuildCells_ReadsSecondsFromMatchingDayKey()
        {
            var stats = new ListeningStats();
            stats.Days["2026-03-15"] = new DayStats { TotalSeconds = 7200 }; // 2 小时 -> 第 3 档

            var cells = ListeningHeatmap.BuildCells(stats, new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 20));
            var cell = cells.Single(c => c.Date == new DateOnly(2026, 3, 15));

            Assert.Equal(7200, cell.Seconds);
            Assert.Equal(3, cell.Level);
        }

        [Fact]
        public void BuildCells_FirstCellIsWeekZero_DayOfWeekMatchesDate()
        {
            var stats = new ListeningStats();
            var from = new DateOnly(2026, 6, 1); // 随便一天
            var cells = ListeningHeatmap.BuildCells(stats, from, from);

            Assert.Single(cells);
            Assert.Equal(0, cells[0].Week);
            Assert.Equal((int)from.DayOfWeek, cells[0].DayOfWeek);
        }

        [Fact]
        public void BuildCells_WeekIncrementsEverySevenDays()
        {
            var stats = new ListeningStats();
            // 从周日开始，跨 3 整周（21 天）
            var sunday = Enumerable.Range(0, 400)
                .Select(i => new DateOnly(2026, 1, 1).AddDays(i))
                .First(d => d.DayOfWeek == DayOfWeek.Sunday);

            var cells = ListeningHeatmap.BuildCells(stats, sunday, sunday.AddDays(20));

            Assert.Equal(0, cells[0].Week);
            Assert.Equal(1, cells[7].Week);
            Assert.Equal(2, cells[14].Week);
            Assert.Equal(2, cells[20].Week); // 第 21 天（索引 20）还在第 3 周（Week=2）内
        }

        [Fact]
        public void BuildCells_FromAfterTo_ReturnsEmpty()
        {
            var stats = new ListeningStats();
            var cells = ListeningHeatmap.BuildCells(stats, new DateOnly(2026, 5, 10), new DateOnly(2026, 5, 1));
            Assert.Empty(cells);
        }
    }
}
