using System;
using System.IO;
using Newtonsoft.Json;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 记两个一次性开关——"用没用过🔀混搭生成过一份草稿"、"用没用过🖌️画板画完过一个图标"，
    /// 给「混音师」「像素画师」这两个成就用。跟「主题工匠」（CustomThemeAchievement.IsUnlocked，
    /// 现算 CustomThemeStore 里还有几个主题）刻意不是同一套判定方式：混搭/画板这两个动作本身
    /// 不落盘（只是把结果塞进 CustomThemeWindow 的输入框，用户可能改完都不点保存），没有一个
    /// 能直接现算的"当前状态"能反推"用没用过"，所以老老实实用一个小 flags 文件记一次性事件——
    /// 用过了就不会因为后来把所有主题都删掉、或者存的主题跟混搭/画板已经没关系了，就又重新锁上。
    /// 这两个是"体验过这个功能"的纪念性成就，不是"手上现在有没有这个东西"的状态性成就。
    /// </summary>
    internal static class CustomThemeFeatureUsage
    {
        private static string FilePath => Path.Combine(CustomThemeStore.Dir, "feature_usage.json");

        private sealed class Flags
        {
            public bool UsedRemix { get; set; }
            public bool UsedPainter { get; set; }
        }

        public static bool HasUsedRemix => Load().UsedRemix;
        public static bool HasUsedPainter => Load().UsedPainter;

        /// <summary>点了「🔀 混搭已存主题」的"生成"按钮、真的拿到一份草稿之后调用——不是"打开了这个窗口"就算，
        /// 是"生成出了结果"才算用过。已经标记过就不重复写盘。</summary>
        public static void MarkRemixUsed() => MarkUsed(f => f.UsedRemix, (f, v) => f.UsedRemix = v);

        /// <summary>在画板里点了"插入到编辑框"或者"复制 JSON 片段"、真的产出了一个图标之后调用——
        /// 只是打开画板看了看又叉掉不算用过。已经标记过就不重复写盘。</summary>
        public static void MarkPainterUsed() => MarkUsed(f => f.UsedPainter, (f, v) => f.UsedPainter = v);

        private static void MarkUsed(Func<Flags, bool> get, Action<Flags, bool> set)
        {
            try
            {
                var flags = Load();
                if (get(flags)) return; // 已经标记过，不用重复写盘
                set(flags, true);
                Directory.CreateDirectory(CustomThemeStore.Dir);
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(flags));
            }
            catch (Exception ex)
            {
                // 标记失败最多是这次成就没点亮，不影响混搭/画板本身已经产出的结果，不用把异常抛给调用方
                AppLog.Error("CustomThemeFeatureUsage.MarkUsed", ex);
            }
        }

        private static Flags Load()
        {
            try
            {
                return File.Exists(FilePath)
                    ? JsonConvert.DeserializeObject<Flags>(File.ReadAllText(FilePath)) ?? new Flags()
                    : new Flags();
            }
            catch
            {
                return new Flags(); // 文件被外部改坏了就当没用过，不是关键数据，不值得为此报错
            }
        }
    }
}
