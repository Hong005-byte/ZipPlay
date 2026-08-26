using System.Linq;

namespace PixelLyric8BitFix
{
    /// <summary>一个称号档位：图标 + 名字 + 进这个档位需要的累计秒数下限。</summary>
    public sealed record RankTier(string Icon, string Name, int MinSeconds);

    /// <summary>
    /// 首页问候语旁边、听歌统计页那个"称号"——按累计听歌时长（全部时间，不分月/年）现算出一个连续的
    /// 等级称号，跟"点亮/没点亮"的二元成就是两种不同的进度反馈：成就是"达成了某个具体目标"，
    /// 称号是"整体在往上涨"的持续感，两者数据源一样（都是 ListeningStatsAggregator.GetTotalSeconds）
    /// 但不冲突——AchievementCalculator 的 7 个常规成就 + 压轴走的是另一条独立的展示逻辑，这里不复用它，
    /// 避免"称号第 5 级"跟"成就点亮了几个"这两套数字混在一起让人分不清哪个对应哪个。
    /// 纯函数，不碰磁盘/UI，方便单元测试。
    /// </summary>
    internal static class ListeningRank
    {
        // 门槛特意跟 AchievementCalculator 的几个听歌成就门槛（1/10/100 小时）错开取整数小时数，
        // 不是同一套刻度——称号是给"持续在涨"的感觉，档位比成就细一些，7 档比 8 个成就的门槛更密。
        // "传奇/尊贵"这类字眼留给「尊贵皇冠」成就和限定皮肤用，这里刻意换了一套不撞名的形容词。
        private static readonly RankTier[] Tiers =
        {
            new("🌱", "初来乍到", 0),
            new("🎧", "渐入佳境", 3600),          // 1 小时
            new("🎵", "老朋友", 36_000),          // 10 小时
            new("🎼", "深度乐迷", 180_000),        // 50 小时
            new("⚡", "百炼成钢", 360_000),        // 100 小时
            new("🔥", "资深发烧友", 1_080_000),    // 300 小时
            new("✨", "音乐之魂", 3_600_000),      // 1000 小时
        };

        /// <summary>当前档位——找不到比 0 更低的门槛，永远至少落在第一档，不会返回 null。</summary>
        public static RankTier GetCurrentTier(int totalSeconds) =>
            Tiers.LastOrDefault(t => totalSeconds >= t.MinSeconds) ?? Tiers[0];

        /// <summary>下一档是谁——已经封顶（在最高档）就是 null，调用方应该显示"已经到顶了"而不是一个进度条。</summary>
        public static RankTier? GetNextTier(int totalSeconds)
        {
            var current = GetCurrentTier(totalSeconds);
            int currentIndex = System.Array.IndexOf(Tiers, current);
            return currentIndex + 1 < Tiers.Length ? Tiers[currentIndex + 1] : null;
        }

        /// <summary>当前档位内的进度，0.0~1.0——封顶了就是 1.0（进度条画满，不是留空或者报错）。
        /// 给称号旁边那条小进度条用，不是精确到秒的东西，纯粹是"还差多少能升一级"的直观提示。</summary>
        public static double GetProgressWithinTier(int totalSeconds)
        {
            var current = GetCurrentTier(totalSeconds);
            var next = GetNextTier(totalSeconds);
            if (next == null) return 1.0;

            double span = next.MinSeconds - current.MinSeconds;
            if (span <= 0) return 1.0; // 理论到不了（门槛表本身是严格递增的），纯防御
            return System.Math.Clamp((totalSeconds - current.MinSeconds) / span, 0.0, 1.0);
        }
    }
}
