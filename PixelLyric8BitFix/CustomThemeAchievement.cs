using System.Collections.Generic;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 跟 AchievementCalculator 的"7 个常规听歌成就 + 1 个压轴"是完全独立的第二条解锁线——4 个都是
    /// 围着"自定义主题"这件事转的动作型成就，条件不是听歌时长/连续天数这些从 ListeningStats 算出来的
    /// 数字，数据源也不一样（CustomThemeStore / CustomThemeFeatureUsage 而不是 ListeningStatsStore）。
    ///
    /// 没有并进 AchievementCalculator.Evaluate 里，是两个刻意的取舍：
    ///   1. 不想让这几个跟自定义主题相关的一次性动作，跟"连续听了多少天"这类听歌习惯指标混进同一份评估——
    ///      AchievementCalculator 的文档注释写明了"不碰磁盘"，混进去就破坏了这条边界。
    ///   2. 不想让它们成为解锁「尊贵皇冠」皮肤的门槛——那套皮肤从 v2.0 起就是"点亮 7 个常规成就"
    ///      这一条路径，混进自定义主题相关的条件会让已经攒了 7 个、只差最后一步的老用户体验变差。
    ///
    /// 四个之中只有「主题工匠」解锁奖励走真实的功能联动（🎲 随机生成的调色板选项池，见 CustomThemeWindow.
    /// BtnRandomize_Click），另外三个（混音师/像素画师/收藏家）跟 AchievementCalculator 里那 7 个常规
    /// 听歌成就一样，纯粹是"记一笔、点亮一张卡片"，没有额外的功能奖励。
    /// </summary>
    internal static class CustomThemeAchievement
    {
        public static readonly AchievementDefinition Definition = new()
        { Id = "custom_theme_maker", Icon = "🎨", Name = "主题工匠", Description = "保存过一个客制化主题" };

        public static readonly AchievementDefinition RemixMaster = new()
        { Id = "custom_theme_remix", Icon = "🔀", Name = "混音师", Description = "用「混搭已存主题」生成过一份草稿" };

        public static readonly AchievementDefinition PixelPainter = new()
        { Id = "custom_theme_painter", Icon = "🖌️", Name = "像素画师", Description = "用「图标画板」画完过一个图标" };

        public static readonly AchievementDefinition Collector = new()
        { Id = "custom_theme_collector", Icon = "📦", Name = "收藏家", Description = $"存满 {CustomThemeStore.MaxThemes} 个客制化主题" };

        /// <summary>「主题工匠」——是不是"现在手上"还留着至少一个客制化主题，删到 0 个会重新锁上，
        /// 见 CustomThemeWindow.RefreshThemeList 里删除按钮那段。</summary>
        public static bool IsUnlocked() => CustomThemeStore.ListAll().Count > 0;

        public static bool IsRemixMasterUnlocked() => CustomThemeFeatureUsage.HasUsedRemix;

        public static bool IsPixelPainterUnlocked() => CustomThemeFeatureUsage.HasUsedPainter;

        /// <summary>「收藏家」——现算，是不是刚好顶到 CustomThemeStore.MaxThemes 这个上限；
        /// 删掉一个降到上限以下会跟「主题工匠」一样重新锁上，两者判定方式保持一致。</summary>
        public static bool IsCollectorUnlocked() => CustomThemeStore.ListAll().Count >= CustomThemeStore.MaxThemes;

        /// <summary>AchievementsWindow 一次性拿全这 4 张卡片，顺序即展示顺序：先是有真实奖励联动的
        /// 「主题工匠」，再是三个纯纪念性的动作型成就。</summary>
        public static List<AchievementProgress> EvaluateAll() => new()
        {
            new() { Achievement = Definition, Unlocked = IsUnlocked() },
            new() { Achievement = RemixMaster, Unlocked = IsRemixMasterUnlocked() },
            new() { Achievement = PixelPainter, Unlocked = IsPixelPainterUnlocked() },
            new() { Achievement = Collector, Unlocked = IsCollectorUnlocked() },
        };
    }
}
