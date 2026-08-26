using System.Linq;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 把 ListeningSummary 里那几个"总结叙事"用的数字（连续天数/不同艺人数/不同歌曲数/最投入的一天）
    /// 拼成一两句人话，而不是让用户自己盯着一堆并排的数字去脑补故事感。ListeningStatsWindow 的总结面板
    /// 和 ShareCardBuilder 的分享卡片都用同一份文案，保证"页面里看到的"和"导出图片里看到的"是一句话。
    /// 纯函数，不碰 UI/磁盘，方便单元测试。
    /// </summary>
    internal static class ListeningStatsNarrative
    {
        /// <summary>数据太单薄（比如只有 1 天记录、什么排行都还没攒出来）就返回 null——调用方应该
        /// 直接不显示这个区块，硬凑一句"连续听了 1 天"这种没有信息量的话还不如不说。</summary>
        public static string? BuildHeadline(ListeningSummary summary)
        {
            string? streakClause = summary.LongestStreakDays >= 2
                ? $"最长一口气连续听了 {summary.LongestStreakDays} 天"
                : null;

            string? varietyClause = summary.UniqueArtistCount > 0
                ? $"认识了 {summary.UniqueArtistCount} 位不同的艺人、听过 {summary.UniqueTrackCount} 首不同的歌"
                : null;

            string? bestDayClause = summary.BestDay.HasValue && summary.BestDaySeconds > 0
                ? $"投入最多的一天是 {summary.BestDay.Value:M月d日}，那天听了 {ListeningStatsAggregator.FormatDuration(summary.BestDaySeconds)}"
                : null;

            // 第一句：连续天数 + 艺人/歌曲多样性拼一起；第二句单独说最投入的一天——两句话题不一样，
            // 硬塞进同一句会读起来很挤。三个 clause 只要有一个是 null 就跳过，不留""，最长空一句"。
            string firstSentence = string.Join("，", new[] { streakClause, varietyClause }.Where(c => c != null));
            string? secondSentence = bestDayClause;

            if (firstSentence.Length == 0 && secondSentence == null) return null;

            string result = firstSentence.Length > 0 ? firstSentence + "。" : "";
            if (secondSentence != null) result += secondSentence + "。";
            return result;
        }
    }
}
