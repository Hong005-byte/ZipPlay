using System;
using System.Globalization;
using System.Linq;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// "🎲 随机生成一份"按钮背后的逻辑：从手工搭配好的调色板/图标/字体里各抽一个，拼成一份完整、
    /// 直接能过校验的自定义主题 JSON，塞进输入框给用户当草稿改，而不是让用户从空白 JSON 或者那份
    /// 海边黄昏示例开始一行行抠颜色。抽的是"选项"不是"数值"——每个 Palette 内部的几个颜色本来就是配好的，
    /// 不会出现文字色跟背景撞在一起看不清这种问题；唯一真随机拼接的是"这次抽到哪个调色板 + 哪个图标形状 +
    /// 哪种动画招式 + 渐变还是纯色"，组合数量足够多但每一种搭配出来都是能看的。
    /// </summary>
    internal static class CustomThemeRandomizer
    {
        private sealed class Palette
        {
            public string Mood = "";
            public string Title = "";
            public string Artist = "";
            public string Accent = "";
            public string Lyric = "";
            public string LyricBoxBg = "";
            public string LyricBoxBorder = "";
            public string[] BgStops = Array.Empty<string>();
        }

        // 8 套手工配好的调色板——文字色/强调色/歌词框颜色/背景渐变站点全是同一份里挑出来的，
        // 保证亮度/色相搭配协调（浅色文字配深色背景、强调色跟渐变里的一个站点同色系），
        // 不会随机组合出"白字配白底"这种抽到就没法用的结果
        private static readonly Palette[] Palettes =
        {
            new Palette { Mood = "暮色海岸", Title = "#FFF3E0", Artist = "#F2C6A0", Accent = "#F9C784", Lyric = "#FFF3E0",
                LyricBoxBg = "#B32A1F40", LyricBoxBorder = "#F9C784", BgStops = new[] { "#F2994A", "#EA7093", "#4A3B78" } },
            new Palette { Mood = "深海极光", Title = "#E0FFF9", Artist = "#9AD9CF", Accent = "#4FD1C5", Lyric = "#E0FFF9",
                LyricBoxBg = "#0B2B3340", LyricBoxBorder = "#4FD1C5", BgStops = new[] { "#0F2027", "#203A43", "#2C5364" } },
            new Palette { Mood = "霓虹午夜", Title = "#F5E8FF", Artist = "#C9A0FF", Accent = "#E040FB", Lyric = "#F5E8FF",
                LyricBoxBg = "#1A0B2E40", LyricBoxBorder = "#E040FB", BgStops = new[] { "#0F0C29", "#302B63", "#24243E" } },
            new Palette { Mood = "森野晨光", Title = "#F0FFE8", Artist = "#B8D9A0", Accent = "#8BC34A", Lyric = "#F0FFE8",
                LyricBoxBg = "#12240B40", LyricBoxBorder = "#8BC34A", BgStops = new[] { "#2E4A1F", "#4F7942", "#A8D08D" } },
            new Palette { Mood = "赤焰篝火", Title = "#FFF0E0", Artist = "#F2A65A", Accent = "#FF6E40", Lyric = "#FFF0E0",
                LyricBoxBg = "#33140640", LyricBoxBorder = "#FF6E40", BgStops = new[] { "#1A0F00", "#7A2E00", "#FF6E40" } },
            new Palette { Mood = "糖果薄荷", Title = "#F0FFFC", Artist = "#B8E8DE", Accent = "#66D9C2", Lyric = "#F0FFFC",
                LyricBoxBg = "#0A2B2640", LyricBoxBorder = "#66D9C2", BgStops = new[] { "#134E4A", "#2DD4BF", "#0F2E2A" } },
            new Palette { Mood = "冰霜雪原", Title = "#F0F6FF", Artist = "#AFC9E8", Accent = "#7FB2E5", Lyric = "#F0F6FF",
                LyricBoxBg = "#0A1B3340", LyricBoxBorder = "#7FB2E5", BgStops = new[] { "#0A1B33", "#1F3B57", "#7FB2E5" } },
            new Palette { Mood = "玫瑰金昏", Title = "#FFF5F0", Artist = "#F2B6A0", Accent = "#F7856A", Lyric = "#FFF5F0",
                LyricBoxBg = "#2A101040", LyricBoxBorder = "#F7856A", BgStops = new[] { "#3B0F1A", "#7A2848", "#C2547A" } },
        };

        // 限定调色板——只有解锁了"主题工匠"成就（保存过至少一个客制化主题，见 CustomThemeAchievement）
        // 才会出现在随机池里，呼应"尊贵皇冠"皮肤同一套"先有产出再给奖励"的思路，只是奖励换成了
        // 随机生成器里的一份配色，不是皮肤。祖母绿 + 暗金，跟尊贵皇冠的紫金配色刻意区分开，
        // 不会让用户把两个不同的解锁系统搞混。
        private static readonly Palette ExclusivePalette = new Palette
        {
            Mood = "祖母绿荣光", Title = "#FFF8E7", Artist = "#E8C77A", Accent = "#D4AF37", Lyric = "#FFF8E7",
            LyricBoxBg = "#0B2A1F40", LyricBoxBorder = "#D4AF37", BgStops = new[] { "#062B1D", "#0E4A32", "#D4AF37" },
        };

        // 内置皮肤实际在用的几款系统字体（见 SkinTheme.cs），保证挑出来的字体不管在谁的电脑上都装了，
        // 不会出现"系统没这个字体，静默回退成默认字体"这种预览和保存后长得不一样的情况
        private static readonly string[] Fonts = { "Segoe UI", "Segoe UI Light", "Segoe UI Semibold", "Georgia", "Consolas", "Lucida Console" };

        // 6 个 8x8 图标形状，占位符 '#'（画主体）/ 'w'（画点缀细节）——具体颜色生成时才填进去，
        // 用抽到的那份调色板的 accent / title 上色，图标配色永远跟整体协调，不用为每个调色板各画一套专属图标
        private static readonly string[][] IconShapes =
        {
            new[] { "........", "..#..#..", ".######.", ".######.", "..####..", "...##...", "........", "........" }, // 心形
            new[] { "...##...", "..####..", ".######.", "########", ".######.", "..####..", "...##...", "........" }, // 钻石
            new[] { "..w..w..", ".wwwwww.", "...##...", "..####..", ".######.", "..####..", "...##...", "........" }, // 星星
            new[] { "........", "..w..w..", ".wwwwww.", "..####..", "..####..", "...##...", "...##...", "........" }, // 叶子
            new[] { "........", "..####..", ".##..##.", "##....##", "##....##", ".##..##.", "..####..", "........" }, // 圆环
            new[] { "..#.....", "..##....", "..###...", "..####..", "..#####.", "..######", "..###...", "........" }, // 音符
        };

        /// <summary>includeExclusive=true 时随机池里多一份 ExclusivePalette（跟其它 8 份概率均等，
        /// 不是"抽完常规再额外判一次"），调用方（CustomThemeWindow）自己决定要不要传 true——
        /// 传不传完全取决于 CustomThemeAchievement.IsUnlocked()，这个方法本身不碰成就/磁盘状态，
        /// 保持纯函数，方便单元测试不用先在磁盘上摆一份已保存的主题才能测。</summary>
        public static string GenerateJson(bool includeExclusive = false)
        {
            var rng = Random.Shared;
            var pool = includeExclusive ? Palettes.Append(ExclusivePalette).ToArray() : Palettes;
            var p = pool[rng.Next(pool.Length)];
            string font = Fonts[rng.Next(Fonts.Length)];
            var icon = IconShapes[rng.Next(IconShapes.Length)];
            string animType = CustomThemeValidator.ValidAnimationTypes[rng.Next(CustomThemeValidator.ValidAnimationTypes.Length)];

            // 8 成概率渐变（更贴近内置皮肤的观感），2 成纯色（凑够 2 个站点才有渐变可选，理论上 BgStops 都是 3 个够用）
            bool gradient = p.BgStops.Length >= 2 && rng.Next(10) < 8;
            string direction = rng.Next(2) == 0 ? "vertical" : "diagonal";
            bool musicReactive = rng.Next(10) < 6; // 6 成概率带上音乐律动，多数时候能看到这个开关长啥效果
            string sensitivity = CustomThemeValidator.ValidSensitivities[rng.Next(CustomThemeValidator.ValidSensitivities.Length)];
            double duration = Math.Round(1.5 + rng.NextDouble() * 3.0, 1);

            string bgStopsJson = gradient
                ? string.Join(", ", p.BgStops.Select(s => $"\"{s}\""))
                : $"\"{p.BgStops[0]}\"";
            string iconRowsJson = string.Join(",\n      ", icon.Select(row => $"\"{row}\""));
            string durationText = duration.ToString("0.0", CultureInfo.InvariantCulture);

            // 4 成概率带一个额外装饰层（贴在跟主图标不同的角，避免叠在一起看不清），复用同一份调色板的
            // accent/title 上色，颜色始终跟整体协调；animType 特意排除 drift/fall——那两招在额外层里退化成
            // 原地小幅摆动（见 MainWindow.Skins.cs 的 StartLayerAnimation），选出来跟 sway/bob 观感差不多，
            // 不如把这两种概率让给单独在主图标上更看得出差别的 pulse/twinkle/sway/spin/flicker/bob
            string layersJson = "";
            if (rng.Next(10) < 4)
            {
                var layerIcon = IconShapes[rng.Next(IconShapes.Length)];
                string[] layerAnimTypes = { "pulse", "twinkle", "sway", "spin", "flicker", "bob" };
                string layerAnimType = layerAnimTypes[rng.Next(layerAnimTypes.Length)];
                string[] anchors = { "top-left", "top-right", "bottom-left", "bottom-right" };
                string anchor = anchors[rng.Next(anchors.Length)];
                double layerDuration = Math.Round(1.2 + rng.NextDouble() * 2.5, 1);
                string layerDurationText = layerDuration.ToString("0.0", CultureInfo.InvariantCulture);
                string layerIconRowsJson = string.Join(",\n        ", layerIcon.Select(row => $"\"{row}\""));

                layersJson = $@",
  ""layers"": [
    {{
      ""anchor"": ""{anchor}"",
      ""icon"": {{
        ""palette"": {{ ""#"": ""{p.Accent}"", ""w"": ""{p.Title}"" }},
        ""rows"": [
          {layerIconRowsJson}
        ]
      }},
      ""animation"": {{ ""type"": ""{layerAnimType}"", ""duration"": {layerDurationText} }}
    }}
  ]";
            }

            return
$@"{{
  ""name"": ""{p.Mood}"",
  ""font"": ""{font}"",
  ""colors"": {{
    ""title"": ""{p.Title}"",
    ""artist"": ""{p.Artist}"",
    ""accent"": ""{p.Accent}"",
    ""lyric"": ""{p.Lyric}"",
    ""glow"": ""{p.Accent}"",
    ""glowBlur"": 4,
    ""lyricBoxBg"": ""{p.LyricBoxBg}"",
    ""lyricBoxBorder"": ""{p.LyricBoxBorder}""
  }},
  ""background"": {{
    ""type"": ""{(gradient ? "gradient" : "solid")}"",
    ""direction"": ""{direction}"",
    ""stops"": [{bgStopsJson}]
  }},
  ""icon"": {{
    ""palette"": {{ ""#"": ""{p.Accent}"", ""w"": ""{p.Title}"" }},
    ""rows"": [
      {iconRowsJson}
    ]
  }},
  ""animation"": {{ ""type"": ""{animType}"", ""duration"": {durationText}, ""musicReactive"": {(musicReactive ? "true" : "false")}, ""sensitivity"": ""{sensitivity}"" }}{layersJson}
}}";
        }
    }
}
