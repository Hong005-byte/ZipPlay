using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// "🎲 随机生成一份"按钮背后的逻辑：从手工搭配好的调色板/图标/字体里各抽一个，拼成一份完整、
    /// 直接能过校验的自定义主题 JSON，塞进输入框给用户当草稿改，而不是让用户从空白 JSON 或者那份
    /// 海边黄昏示例开始一行行抠颜色。抽的是"选项"不是"数值"——每个 Palette 内部的几个颜色本来就是配好的，
    /// 不会出现文字色跟背景撞在一起看不清这种问题；唯一真随机拼接的是"这次抽到哪个调色板 + 哪个图标形状 +
    /// 哪种动画招式 + 渐变还是纯色 + 要不要带 icon.actions/额外装饰层"，组合数量足够多但每一种搭配出来都是能看的。
    /// icon.actions（点击切换的额外姿势，含它自己的 animation/autoSwitchAfterSeconds）也接进了这套概率池，
    /// 不然这一整套系统只能靠手写 JSON 或者找 AI 生成才能体验到，普通用户点一下按钮就该有机会撞见。
    /// </summary>
    /// <summary>调色板稀有度——纯粹是"🎲 随机生成"抽到的时候要不要多弹一句"出货"反馈，跟主题本身
    /// 能不能保存/校验通不通过完全无关，Common 跟 Limited 生成出来的 JSON 结构是一模一样的。
    /// 顺序即"稀有程度递增"，UI 层可以直接用这个顺序判断"要不要庆祝"。</summary>
    public enum PaletteRarity { Common, Rare, Epic, Limited }

    public static class CustomThemeRandomizer
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
            public PaletteRarity Rarity = PaletteRarity.Common;

            // 抽奖池里这份调色板占多少份额，不是百分比——PickWeighted 按权重之和做加权随机。
            // 同一档稀有度里各份权重相等，权重比大致是 常见:稀有:史诗 = 14:11:8（约 70%:22%:8%），
            // 一眼就能看出"越稀有权重越低"，不用去凑一个刚好等于 100 的复杂数字
            public int Weight = 14;
        }

        // 8 套手工配好的调色板——文字色/强调色/歌词框颜色/背景渐变站点全是同一份里挑出来的，
        // 保证亮度/色相搭配协调（浅色文字配深色背景、强调色跟渐变里的一个站点同色系），
        // 不会随机组合出"白字配白底"这种抽到就没法用的结果。5 份 Common / 2 份 Rare / 1 份 Epic——
        // 稀有度纯粹是主观挑的"这份配色视觉上更抓眼"（高饱和撞色、更暗的对比背景），不是随便分的。
        private static readonly Palette[] Palettes =
        {
            new Palette { Mood = "暮色海岸", Title = "#FFF3E0", Artist = "#F2C6A0", Accent = "#F9C784", Lyric = "#FFF3E0",
                LyricBoxBg = "#B32A1F40", LyricBoxBorder = "#F9C784", BgStops = new[] { "#F2994A", "#EA7093", "#4A3B78" },
                Rarity = PaletteRarity.Common, Weight = 14 },
            new Palette { Mood = "深海极光", Title = "#E0FFF9", Artist = "#9AD9CF", Accent = "#4FD1C5", Lyric = "#E0FFF9",
                LyricBoxBg = "#0B2B3340", LyricBoxBorder = "#4FD1C5", BgStops = new[] { "#0F2027", "#203A43", "#2C5364" },
                Rarity = PaletteRarity.Rare, Weight = 11 },
            new Palette { Mood = "霓虹午夜", Title = "#F5E8FF", Artist = "#C9A0FF", Accent = "#E040FB", Lyric = "#F5E8FF",
                LyricBoxBg = "#1A0B2E40", LyricBoxBorder = "#E040FB", BgStops = new[] { "#0F0C29", "#302B63", "#24243E" },
                Rarity = PaletteRarity.Epic, Weight = 8 },
            new Palette { Mood = "森野晨光", Title = "#F0FFE8", Artist = "#B8D9A0", Accent = "#8BC34A", Lyric = "#F0FFE8",
                LyricBoxBg = "#12240B40", LyricBoxBorder = "#8BC34A", BgStops = new[] { "#2E4A1F", "#4F7942", "#A8D08D" },
                Rarity = PaletteRarity.Common, Weight = 14 },
            new Palette { Mood = "赤焰篝火", Title = "#FFF0E0", Artist = "#F2A65A", Accent = "#FF6E40", Lyric = "#FFF0E0",
                LyricBoxBg = "#33140640", LyricBoxBorder = "#FF6E40", BgStops = new[] { "#1A0F00", "#7A2E00", "#FF6E40" },
                Rarity = PaletteRarity.Common, Weight = 14 },
            new Palette { Mood = "糖果薄荷", Title = "#F0FFFC", Artist = "#B8E8DE", Accent = "#66D9C2", Lyric = "#F0FFFC",
                LyricBoxBg = "#0A2B2640", LyricBoxBorder = "#66D9C2", BgStops = new[] { "#134E4A", "#2DD4BF", "#0F2E2A" },
                Rarity = PaletteRarity.Common, Weight = 14 },
            new Palette { Mood = "冰霜雪原", Title = "#F0F6FF", Artist = "#AFC9E8", Accent = "#7FB2E5", Lyric = "#F0F6FF",
                LyricBoxBg = "#0A1B3340", LyricBoxBorder = "#7FB2E5", BgStops = new[] { "#0A1B33", "#1F3B57", "#7FB2E5" },
                Rarity = PaletteRarity.Rare, Weight = 11 },
            new Palette { Mood = "玫瑰金昏", Title = "#FFF5F0", Artist = "#F2B6A0", Accent = "#F7856A", Lyric = "#FFF5F0",
                LyricBoxBg = "#2A101040", LyricBoxBorder = "#F7856A", BgStops = new[] { "#3B0F1A", "#7A2848", "#C2547A" },
                Rarity = PaletteRarity.Common, Weight = 14 },
        };

        // 限定调色板——只有解锁了"主题工匠"成就（保存过至少一个客制化主题，见 CustomThemeAchievement）
        // 才会出现在随机池里，呼应"尊贵皇冠"皮肤同一套"先有产出再给奖励"的思路，只是奖励换成了
        // 随机生成器里的一份配色，不是皮肤。祖母绿 + 暗金，跟尊贵皇冠的紫金配色刻意区分开，
        // 不会让用户把两个不同的解锁系统搞混。权重压到 5（比 Epic 的 8 还低），解锁之后也不是随便一抽
        // 就出，得多试几次，"限定"这个字才配得上这个稀有度。
        private static readonly Palette ExclusivePalette = new Palette
        {
            Mood = "祖母绿荣光", Title = "#FFF8E7", Artist = "#E8C77A", Accent = "#D4AF37", Lyric = "#FFF8E7",
            LyricBoxBg = "#0B2A1F40", LyricBoxBorder = "#D4AF37", BgStops = new[] { "#062B1D", "#0E4A32", "#D4AF37" },
            Rarity = PaletteRarity.Limited, Weight = 5,
        };

        // 按权重之和做加权随机——把候选按权重挤成一个总长度是"权重和"的数轴，掷一个 [0, 总长) 的骰子，
        // 骰子落在谁的区间就是谁。比"复制成一个展开列表再均匀抽"省内存，权重跟稀有度分布也更容易在
        // 上面 Palettes 数组里一眼看出来（不用去数每个 Mood 在展开列表里重复了几次）。
        private static Palette PickWeighted(Random rng, IReadOnlyList<Palette> pool)
        {
            int totalWeight = pool.Sum(p => p.Weight);
            int roll = rng.Next(totalWeight);
            int cumulative = 0;
            foreach (var p in pool)
            {
                cumulative += p.Weight;
                if (roll < cumulative) return p;
            }
            return pool[^1]; // 理论到不了（权重之和已经等于 totalWeight，roll 严格小于它），纯防御
        }

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

        // 9 招式各自实际控制的是哪个属性——两招落在同一个属性上组合起来没意义（后一个直接盖掉前一个），
        // 抽组合的时候只从"不同属性"里选第二招，保证抽出来的组合真的是"两个效果叠加"而不是"抽了个寂寞"。
        // 主图标那边 drift/fall/walk 根本不进 ComboablePool（这三招各自要专属的渲染结构，不能组合，
        // 校验会拦），层里没有这个限制（那三招在层里都退化成同一套单图标动画），层的组合逻辑直接从
        // ComboablePool 这 6 招里挑，不含 drift/fall/walk——纯粹是"这三招在层里效果跟 sway/bob 分不
        // 太出来"，见下面随机装饰层那段的注释，不是校验层面不让组合。walk 没有单独列进这个字典——
        // 它在层里退化成跟 drift 完全一样的动画（StartLayerAnimation 里 "drift"/"walk" 共用同一个
        // case），属性分组用 drift 那份即可，不需要重复一份。
        private static readonly Dictionary<string, string> AnimationPropertyGroup = new()
        {
            ["pulse"] = "glow", ["flicker"] = "glow",
            ["sway"] = "rotate", ["spin"] = "rotate",
            ["twinkle"] = "opacity",
            ["bob"] = "translateY", ["fall"] = "translateY",
            ["drift"] = "translateX",
        };
        private static readonly string[] ComboablePool = { "pulse", "twinkle", "sway", "spin", "flicker", "bob" };

        /// <summary>includeExclusive=true 时随机池里多一份 ExclusivePalette（按它自己的权重参与加权抽取，
        /// 不是"抽完常规再额外判一次"），调用方（CustomThemeWindow）自己决定要不要传 true——
        /// 传不传完全取决于 CustomThemeAchievement.IsUnlocked()，这个方法本身不碰成就/磁盘状态，
        /// 保持纯函数，方便单元测试不用先在磁盘上摆一份已保存的主题才能测。
        /// 只要 JSON、不关心这次抽到的稀有度的调用方用这个；要给用户一句"出货"反馈的用下面的
        /// <see cref="GenerateWithRarity"/>——两个方法是同一份生成逻辑，这个只是取 .Json 的薄封装，
        /// 保留是因为已有的单元测试和这个签名绑定，换名字/改返回类型没必要。</summary>
        public static string GenerateJson(bool includeExclusive = false) => GenerateWithRarity(includeExclusive).Json;

        /// <summary>抽出来的主题连同"这次抽到的调色板叫什么名字、什么稀有度"一起吐出来——
        /// CustomThemeWindow.BtnRandomize_Click 拿 Rarity 决定要不要弹一句"稀有/史诗/限定"的额外提示，
        /// Common 不用特别庆祝，抽到就是最常见的结果。</summary>
        public static RandomThemeResult GenerateWithRarity(bool includeExclusive = false)
        {
            var rng = Random.Shared;
            var pool = includeExclusive ? Palettes.Append(ExclusivePalette).ToArray() : Palettes;
            var p = PickWeighted(rng, pool);
            string font = Fonts[rng.Next(Fonts.Length)];
            var icon = IconShapes[rng.Next(IconShapes.Length)];
            string animType = CustomThemeValidator.ValidAnimationTypes[rng.Next(CustomThemeValidator.ValidAnimationTypes.Length)];

            // 4 成概率给主图标也配一个组合招式——只在抽到的不是 drift/fall/walk 时才有意义（这三招各自
            // 要用专属的渲染结构，不能组合，见 CustomThemeValidator.ValidateAnimation），而且只从
            // "控制的是不同属性"的招式里挑第二个：挑同属性的（比如 pulse 又挑 flicker，两个都是控制
            // 发光度）后一个会直接盖掉前一个，抽出来的组合毫无意义，不如干脆别抽
            if (animType != "drift" && animType != "fall" && animType != "walk" && rng.Next(10) < 4)
            {
                var candidates = ComboablePool.Where(t => t != animType && AnimationPropertyGroup[t] != AnimationPropertyGroup[animType]).ToArray();
                if (candidates.Length > 0) animType = $"{animType}+{candidates[rng.Next(candidates.Length)]}";
            }

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
                string layerAnimType = ComboablePool[rng.Next(ComboablePool.Length)];
                // 层没有主图标 drift/fall 那个结构性限制，但这里仍然只从同一个 6 招池子里挑组合——
                // 跟上面排除 drift/fall 的理由一样，纯粹是"这两招在层里效果跟 sway/bob 分不太出来"，
                // 不是校验层面不让组合
                if (rng.Next(10) < 4)
                {
                    var comboCandidates = ComboablePool.Where(t => t != layerAnimType && AnimationPropertyGroup[t] != AnimationPropertyGroup[layerAnimType]).ToArray();
                    if (comboCandidates.Length > 0) layerAnimType = $"{layerAnimType}+{comboCandidates[rng.Next(comboCandidates.Length)]}";
                }
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

            // icon.actions（可选，约 4 成概率）：点一下装饰图标能循环切换的额外姿势。以前这套系统
            // （包括 walk 这种需要专属渲染轨道的招式）只能靠手写 JSON 或者找 AI 生成才能体验到，
            // 随机生成器从来不带——现在接进同一个概率池子，普通用户点一下"🎲 随机生成"就有机会直接
            // 看到效果，不用先弄懂 icon.actions 长什么样。形状特意换一个跟主图标不一样的（IconShapes
            // 挑不同下标），点击切换时画面才看得出真的变了，不是切了个几乎一样的姿势。
            string actionsJson = "";
            if (rng.Next(10) < 4)
            {
                int actionCount = rng.Next(10) < 3 ? 2 : 1; // 大部分时候 1 个，小概率给 2 个，绕一圈能看到两种额外姿势
                var actionBlocks = new List<string>(actionCount);
                for (int i = 0; i < actionCount; i++)
                {
                    // 形状不够用的极端情况（IconShapes 只有 6 种、已经抽走了主图标 + 上一个动作）就允许
                    // 重复——这不是关键功能，不值得为了"绝不重样"专门维护一份已用形状的排除列表
                    var actionIconCandidates = IconShapes.Where(s => s != icon).ToArray();
                    var actionIcon = actionIconCandidates.Length > 0 ? actionIconCandidates[rng.Next(actionIconCandidates.Length)] : IconShapes[rng.Next(IconShapes.Length)];
                    string actionIconRowsJson = string.Join(",\n          ", actionIcon.Select(row => $"\"{row}\""));

                    string actionExtra = "";

                    // 3 成概率给这个动作单独配一套移动方式——跟主图标同一套抽招式的逻辑（含 4 成概率
                    // 组合、drift/fall/walk 独占），是这三招（尤其是 walk，装饰栏里来回走）目前唯一
                    // 会被随机抽到的地方，不然 walk 这招永远只能靠手写 JSON 才用得上
                    if (rng.Next(10) < 3)
                    {
                        string actionAnimType = CustomThemeValidator.ValidAnimationTypes[rng.Next(CustomThemeValidator.ValidAnimationTypes.Length)];
                        if (actionAnimType != "drift" && actionAnimType != "fall" && actionAnimType != "walk" && rng.Next(10) < 4)
                        {
                            var comboCandidates = ComboablePool.Where(t => t != actionAnimType && AnimationPropertyGroup[t] != AnimationPropertyGroup[actionAnimType]).ToArray();
                            if (comboCandidates.Length > 0) actionAnimType = $"{actionAnimType}+{comboCandidates[rng.Next(comboCandidates.Length)]}";
                        }
                        string actionAnimDurationText = Math.Round(1.2 + rng.NextDouble() * 2.5, 1).ToString("0.0", CultureInfo.InvariantCulture);
                        actionExtra += $@",
      ""animation"": {{ ""type"": ""{actionAnimType}"", ""duration"": {actionAnimDurationText} }}";
                    }

                    // 4 成概率带自动切换阈值——不用点击，连续听满这么多秒自动切过去。30~180 秒之间给个
                    // 随手挂着播放器就能等到、体验到效果的范围，太长的话这个字段等于白抽了
                    if (rng.Next(10) < 4)
                    {
                        string autoSwitchSecondsText = Math.Round(30 + rng.NextDouble() * 150, 0).ToString("0", CultureInfo.InvariantCulture);
                        actionExtra += $@",
      ""autoSwitchAfterSeconds"": {autoSwitchSecondsText}";
                    }

                    // 4 成概率带过渡淡化——只在切换前后待在同一条轨道时才真的生效（见
                    // CustomThemeIconAction.TransitionSeconds 的注释），但随机器这里不知道点击时上一个
                    // 动作是谁，独立抽这个字段就好：抽到不生效的组合也不算错，只是这一次点击刚好碰不上
                    // 交叉淡化的条件，跟这个字段本身填了正数、能过校验没关系。0.5~2.5 秒之间，太短感觉
                    // 不出过渡、太长又会让人怀疑"是不是卡住了"
                    if (rng.Next(10) < 4)
                    {
                        string transitionSecondsText = Math.Round(0.5 + rng.NextDouble() * 2.0, 1).ToString("0.0", CultureInfo.InvariantCulture);
                        actionExtra += $@",
      ""transitionSeconds"": {transitionSecondsText}";
                    }

                    actionBlocks.Add($@"    {{
      ""name"": ""动作 {i + 1}"",
      ""frames"": [
        [
          {actionIconRowsJson}
        ]
      ]{actionExtra}
    }}");
                }
                actionsJson = $@",
    ""actions"": [
{string.Join(",\n", actionBlocks)}
    ]";
            }

            string json =
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
    ]{actionsJson}
  }},
  ""animation"": {{ ""type"": ""{animType}"", ""duration"": {durationText}, ""musicReactive"": {(musicReactive ? "true" : "false")}, ""sensitivity"": ""{sensitivity}"" }}{layersJson}
}}";

            return new RandomThemeResult(json, p.Mood, p.Rarity);
        }
    }

    /// <summary>CustomThemeRandomizer.GenerateWithRarity 的结果——JSON 草稿 + 这次抽到的调色板名字/稀有度，
    /// 给"🎲 随机生成"按钮弹"出货"反馈用（Rare 以上才值得特别提一句）。</summary>
    public sealed record RandomThemeResult(string Json, string Mood, PaletteRarity Rarity);
}
