using System.Collections.Generic;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// "✨ 查看亮点回顾"背后的纯逻辑——把一份 ListeningSummary 拆成几张"一句话卡片"（图标 + 大字标题 +
    /// 小字说明），仿的是各家音乐 App 常见的"年度/月度总结"翻页效果，跟 ListeningStatsNarrative（拼成
    /// 一整段话）是同一份数据的两种呈现：这边是"一张卡片说一件事，翻页看"，那边是"一段话里带过"，
    /// 页面里两个入口都留着，不是互相替代。不碰 UI/磁盘，方便单元测试；ListeningHighlightsWindow 只管
    /// 照着这份卡片列表一张张翻。
    /// </summary>
    internal static class ListeningHighlightsBuilder
    {
        public sealed record HighlightSlide(string Icon, string Headline, string Subtext);

        /// <summary>封面卡片永远在（哪怕总时长是 0——调用方应该在完全没数据时压根不提供这个入口，
        /// 见 ListeningStatsWindow.BtnHighlights 的 IsEnabled），后面几张各自数据不够（比如榜单是空的、
        /// 从没有"最投入的一天"）就跳过，不硬凑一张没内容的卡片。</summary>
        public static List<HighlightSlide> BuildSlides(ListeningSummary summary, string periodLabel, string? userName)
        {
            var slides = new List<HighlightSlide>();

            string coverHeadline = string.IsNullOrWhiteSpace(userName) ? $"{periodLabel}听歌回顾" : $"{userName} 的{periodLabel}听歌回顾";
            slides.Add(new HighlightSlide("🎧", coverHeadline, $"累计听了 {ListeningStatsAggregator.FormatDuration(summary.TotalSeconds)}"));

            if (summary.ActiveDayCount > 0)
            {
                string sub = summary.LongestStreakDays >= 2
                    ? $"最长一口气连续听了 {summary.LongestStreakDays} 天"
                    : "还没攒出连续听歌的记录，明天也来一首？";
                slides.Add(new HighlightSlide("📅", $"{summary.ActiveDayCount} 天有听歌", sub));
            }

            if (summary.TopArtists.Count > 0)
            {
                var top = summary.TopArtists[0];
                slides.Add(new HighlightSlide("🎤", top.Artist, $"最常听的艺人 · {ListeningStatsAggregator.FormatDuration(top.Seconds)}"));
            }

            if (summary.TopTracks.Count > 0)
            {
                var top = summary.TopTracks[0];
                string name = string.IsNullOrEmpty(top.Artist) ? top.Title : $"{top.Title} · {top.Artist}";
                slides.Add(new HighlightSlide("🎵", name, $"单曲循环最多 · {ListeningStatsAggregator.FormatDuration(top.Seconds)}"));
            }

            if (summary.BestDay.HasValue && summary.BestDaySeconds > 0)
            {
                slides.Add(new HighlightSlide("🔥", $"{summary.BestDay.Value:M月d日}",
                    $"投入最多的一天 · 听了 {ListeningStatsAggregator.FormatDuration(summary.BestDaySeconds)}"));
            }

            if (summary.UniqueArtistCount > 0)
            {
                slides.Add(new HighlightSlide("🌟", $"{summary.UniqueArtistCount} 位艺人 · {summary.UniqueTrackCount} 首歌",
                    "这段时间听过的不同艺人和不同歌曲"));
            }

            return slides;
        }
    }
}
