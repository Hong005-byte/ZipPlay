using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// Mini 模式成就环的纯几何/配色计算——给一份 AchievementCalculator.Evaluate() 的结果 + 环的中心/半径/
    /// 强调色，算出每个点该画在哪、什么颜色、多亮，不碰任何 WPF 控件（Rectangle/Canvas）。
    /// MainWindow.MiniMode.cs 的 RefreshAchievementRing 负责"把这些数字变成真的 UI 元素"，这里只管算数，
    /// 方便单元测试——构造一个真的 MainWindow 会牵扯全局热键/托盘图标/SMTC 会话这些有真实副作用的东西，
    /// 不适合在测试里做，纯函数抽出来就不用碰那些。
    /// </summary>
    internal static class MiniAchievementRing
    {
        public readonly record struct DotSpec(double CenterX, double CenterY, Color Color, double Opacity);

        // 故意不用当前皮肤的强调色——音频粒子环本来就是拿强调色画的一圈方点，成就环要是也用同一个
        // 颜色，两圈会糊在一起分不清"这颗到底是律动还是成就"（离屏截图实测过，糊成一片，看不出
        // 8 个点里亮了几个）。改成固定的暖白色，跟 app 里标题文字同一个颜色（#F2F5F1），
        // 不管当前皮肤强调色是什么都能一眼跟音频粒子分开——那边是"当前皮肤的颜色"，这边是"app 自己的颜色"。
        public static readonly Color UnlockedRegularColor = Color.FromRgb(0xF2, 0xF5, 0xF1);

        // 压轴成就（尊贵听众）解锁之后用金色，跟成就墙那张压轴卡片的边框是同一个颜色，
        // 呼应"这张比其它 7 张更特殊"这个既有的视觉语言，不是另外发明一套配色
        public static readonly Color CapstoneGoldColor = Color.FromRgb(0xE6, 0xB6, 0x55);

        private const double UnlockedRegularOpacity = 0.75;
        private const double UnlockedCapstoneOpacity = 0.95;
        // 没解锁的点留一个若隐若现的暗点，暗示"这里还有一个坑位"，不是完全不画——完全不画的话
        // 没法感知到"总共有 8 个"这件事，看到的只会是"解锁了几个就有几个点"，缺了参照系
        private const double LockedOpacity = 0.12;

        /// <summary>progress 是 AchievementCalculator.Evaluate() 的原始结果——固定顺序：前面若干个常规成就 +
        /// 最后一个压轴。从 12 点钟方向开始顺时针在 canvasCenter 为圆心、radius 为半径的圆周上均匀摆开。</summary>
        public static List<DotSpec> BuildDots(IReadOnlyList<AchievementProgress> progress, double canvasCenter, double radius)
        {
            var dots = new List<DotSpec>();
            if (progress.Count == 0) return dots;

            for (int i = 0; i < progress.Count; i++)
            {
                bool isCapstone = i == progress.Count - 1;
                bool unlocked = progress[i].Unlocked;
                double angle = i * (2 * Math.PI / progress.Count) - Math.PI / 2;
                double cx = canvasCenter + radius * Math.Cos(angle);
                double cy = canvasCenter + radius * Math.Sin(angle);

                Color color = isCapstone && unlocked ? CapstoneGoldColor : UnlockedRegularColor;
                double opacity = unlocked ? (isCapstone ? UnlockedCapstoneOpacity : UnlockedRegularOpacity) : LockedOpacity;

                dots.Add(new DotSpec(cx, cy, color, opacity));
            }
            return dots;
        }
    }
}
