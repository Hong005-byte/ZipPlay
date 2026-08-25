namespace PixelLyric8BitFix
{
    /// <summary>
    /// 跟 AchievementCalculator 的"7 个常规听歌成就 + 1 个压轴"是完全独立的第二条解锁线——条件不是
    /// 听歌时长/连续天数这些从 ListeningStats 算出来的数字，是"有没有存过客制化主题"这个动作本身，
    /// 数据源也不一样（CustomThemeStore 而不是 ListeningStatsStore）。
    ///
    /// 没有并进 AchievementCalculator.Evaluate 里，是两个刻意的取舍：
    ///   1. 不想让"存过一个自定义主题"这种一次性动作，跟"连续听了多少天"这类听歌习惯指标混进同一份评估——
    ///      AchievementCalculator 的文档注释写明了"不碰磁盘"，混进去就破坏了这条边界。
    ///   2. 不想让它成为解锁「尊贵皇冠」皮肤的第 8 个门槛——那套皮肤从 v2.0 起就是"点亮 7 个常规成就"
    ///      这一条路径，半路改成 8 个会让已经攒了 7 个、只差最后一步的老用户体验变差。
    /// 解锁奖励也走完全不同的地方（🎲 随机生成一份按钮的调色板选项池，见 CustomThemeWindow.BtnRandomize_Click），
    /// 不是皮肤，两条线除了"都叫成就"之外没有任何耦合。
    /// </summary>
    internal static class CustomThemeAchievement
    {
        public static readonly AchievementDefinition Definition = new()
        { Id = "custom_theme_maker", Icon = "🎨", Name = "主题工匠", Description = "保存过一个客制化主题" };

        public static bool IsUnlocked() => CustomThemeStore.ListAll().Count > 0;
    }
}
