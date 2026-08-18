using System.Collections.Generic;
using System.Linq;

namespace PixelLyric8BitFix
{
    /// <summary>一条成就的静态定义：图标/名字/条件文案，不带"有没有解锁"这个状态——那是算出来的，见 AchievementProgress。</summary>
    public sealed class AchievementDefinition
    {
        public required string Id { get; init; }
        public required string Icon { get; init; }
        public required string Name { get; init; }
        public required string Description { get; init; }
    }

    public sealed class AchievementProgress
    {
        public required AchievementDefinition Achievement { get; init; }
        public required bool Unlocked { get; init; }
    }

    /// <summary>
    /// 成就墙的纯计算部分：给一份 ListeningStats，算出 8 个成就各自解不解锁。不碰磁盘、不碰 UI，
    /// 跟 ListeningStatsAggregator 是同一个套路——所有成就都是从已有的统计数据现算出来的，没有独立的
    /// "已解锁"持久化状态，stats.json 清空了成就自然跟着清零，不会出现"成就已解锁但统计数据是空的"
    /// 这种自相矛盾的状态。
    /// </summary>
    internal static class AchievementCalculator
    {
        public static readonly AchievementDefinition FirstHour = new()
        { Id = "first_hour", Icon = "🎧", Name = "初次启程", Description = "累计听满 1 小时" };
        public static readonly AchievementDefinition TenHours = new()
        { Id = "ten_hours", Icon = "⏱️", Name = "十小时纪念", Description = "累计听满 10 小时" };
        public static readonly AchievementDefinition WeekStreak = new()
        { Id = "week_streak", Icon = "📅", Name = "一周陪伴", Description = "连续 7 天都听歌" };
        public static readonly AchievementDefinition TenArtists = new()
        { Id = "ten_artists", Icon = "🎤", Name = "音乐探索家", Description = "听过 10 位不同艺人" };
        public static readonly AchievementDefinition ThirtyTracks = new()
        { Id = "thirty_tracks", Icon = "🎵", Name = "曲库达人", Description = "听过 30 首不同歌曲" };
        public static readonly AchievementDefinition HundredHours = new()
        { Id = "hundred_hours", Icon = "⏳", Name = "百小时纪念", Description = "累计听满 100 小时" };
        public static readonly AchievementDefinition MonthStreak = new()
        { Id = "month_streak", Icon = "🔥", Name = "连续一月", Description = "连续 30 天都听歌" };

        // 压轴成就：不是靠某个数值门槛，是"前面 7 个全部点亮"——解锁「尊贵皇冠」限定皮肤
        // （PlayerSkin.Crown），这是这套皮肤唯一的解锁路径，见 IsCrownSkinUnlocked
        public static readonly AchievementDefinition CrownSkin = new()
        { Id = "crown_skin", Icon = "👑", Name = "尊贵听众", Description = "解锁以上全部成就" };

        // 顺序即展示顺序：前 7 个是常规成就，压轴那个单独加在 Evaluate 末尾，
        // 方便调用方（成就墙 UI）区分"这张卡片是不是那个特殊的压轴卡片"
        private static readonly AchievementDefinition[] RegularAchievements =
        {
            FirstHour, TenHours, WeekStreak, TenArtists, ThirtyTracks, HundredHours, MonthStreak,
        };

        public static List<AchievementProgress> Evaluate(ListeningStats stats)
        {
            int totalSeconds = ListeningStatsAggregator.GetTotalSeconds(stats, System.DateOnly.MinValue, System.DateOnly.MaxValue);
            int longestStreak = ListeningStatsAggregator.GetLongestStreakDays(stats);
            int uniqueArtists = ListeningStatsAggregator.GetUniqueArtistCount(stats);
            int uniqueTracks = ListeningStatsAggregator.GetUniqueTrackCount(stats);

            var results = new List<AchievementProgress>
            {
                new() { Achievement = FirstHour, Unlocked = totalSeconds >= 3600 },
                new() { Achievement = TenHours, Unlocked = totalSeconds >= 36_000 },
                new() { Achievement = WeekStreak, Unlocked = longestStreak >= 7 },
                new() { Achievement = TenArtists, Unlocked = uniqueArtists >= 10 },
                new() { Achievement = ThirtyTracks, Unlocked = uniqueTracks >= 30 },
                new() { Achievement = HundredHours, Unlocked = totalSeconds >= 360_000 },
                new() { Achievement = MonthStreak, Unlocked = longestStreak >= 30 },
            };

            bool allRegularUnlocked = results.All(r => r.Unlocked);
            results.Add(new AchievementProgress { Achievement = CrownSkin, Unlocked = allRegularUnlocked });
            return results;
        }

        /// <summary>SkinPickerWindow 判断皮肤选择器里 Crown 卡片要不要锁住，直接问这个就够了，
        /// 不用自己重新调 Evaluate 再翻列表找最后一条。</summary>
        public static bool IsCrownSkinUnlocked(ListeningStats stats)
        {
            var results = Evaluate(stats);
            return results.Count > 0 && results[^1].Unlocked;
        }

        /// <summary>还差几个常规成就才能解锁 Crown——皮肤选择器锁住的卡片上用来提示"还差 N 个"。</summary>
        public static int CountRemainingForCrown(ListeningStats stats) =>
            RegularAchievements.Length - Evaluate(stats).Count(p => p.Achievement.Id != CrownSkin.Id && p.Unlocked);

        /// <summary>SkinPickerWindow 同时需要"解锁了没"和"还差几个"这两个问题的答案，都是基于同一次
        /// Evaluate() 结果——用这个方法一次问齐，不要像分别调 IsCrownSkinUnlocked + CountRemainingForCrown
        /// 那样对同一份 stats 重复跑两遍评估（Evaluate 本身要把 stats.Days 完整扫好几遍，不便宜）。</summary>
        public static (bool Unlocked, int Remaining) EvaluateCrownLock(ListeningStats stats)
        {
            var results = Evaluate(stats);
            bool unlocked = results.Count > 0 && results[^1].Unlocked;
            int remaining = RegularAchievements.Length - results.Count(p => p.Achievement.Id != CrownSkin.Id && p.Unlocked);
            return (unlocked, remaining);
        }
    }
}
