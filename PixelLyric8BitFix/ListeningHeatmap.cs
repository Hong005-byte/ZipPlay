using System;
using System.Collections.Generic;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 听歌统计页"🔥 听歌热力图"背后的纯计算——把 ListeningStats 摊成"某一天听了多少秒 -> 落在哪个
    /// 强度档位（0~4）"，再按 GitHub 贡献图那种"横向按周分列、纵向按星期几分行"的摆位方式给出每个
    /// 格子的坐标，UI 只管照着这份坐标画格子，不用自己算某个日期落在第几周第几行。不碰 UI/磁盘，
    /// 跟 KaraokeTiming / AudioVisualizerMath 是同一个套路，方便单元测试。
    /// </summary>
    internal static class ListeningHeatmap
    {
        // 强度档位边界（秒）：0 完全没听，超过第 N 个门槛就是第 N 档（1~4）。用固定阈值而不是"相对
        // 这段范围里最高的一天算比例"，是为了让"档位 3 是什么感觉"每年、每个人都一样——不会出现
        // "去年随便听听就顶格，今年听爆了才刚到 2 档"这种因为分母（谁是当年最高）变了导致的观感不一致。
        private static readonly int[] Thresholds = { 1, 15 * 60, 60 * 60, 3 * 3600 };

        public static int GetIntensityLevel(int totalSeconds)
        {
            int level = 0;
            foreach (int threshold in Thresholds)
            {
                if (totalSeconds < threshold) break;
                level++;
            }
            return level; // 0~4
        }

        public sealed record Cell(DateOnly Date, int Seconds, int Level, int Week, int DayOfWeek);

        /// <summary>[from, to]（含两端）范围内每一天一个格子，哪怕这天完全没听（Level=0）也照样给一格——
        /// 热力图要的是"这一整段范围铺满"，不是只画有数据的那几天。Week 从 0 开始数，是"距 from 所在
        /// 那一周（周日为一周的第一天）过了几周"；DayOfWeek 用 .NET 默认的 0=周日~6=周六。
        /// from 大于 to 直接给空列表，不抛异常——调用方（UI 层）没必要为这个边界情况单独判断。</summary>
        public static List<Cell> BuildCells(ListeningStats stats, DateOnly from, DateOnly to)
        {
            var cells = new List<Cell>();
            if (to < from) return cells;

            DateOnly weekStart = from.AddDays(-(int)from.DayOfWeek); // from 所在那一周的周日
            for (DateOnly date = from; date <= to; date = date.AddDays(1))
            {
                string dayKey = date.ToString("yyyy-MM-dd");
                int seconds = stats.Days.TryGetValue(dayKey, out var day) ? day.TotalSeconds : 0;
                int week = (date.DayNumber - weekStart.DayNumber) / 7; // weekStart 已经对齐到周日，每过 7 天整数进一周
                cells.Add(new Cell(date, seconds, GetIntensityLevel(seconds), week, (int)date.DayOfWeek));
            }
            return cells;
        }
    }
}
