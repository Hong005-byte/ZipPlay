using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 用户自己写的客制化主题——纯数据，不是代码。用户填的是颜色/渐变/图标像素格/挑一个内置动画"招式"，
    /// app 只负责"读数据画图"，用户没法让 app 做数据描述之外的任何事。
    /// 对应"自定义主题"页面里让用户粘贴的那份 JSON。
    /// </summary>
    public sealed class CustomTheme
    {
        public string? Name { get; set; }
        public string? Font { get; set; }
        public CustomThemeColors? Colors { get; set; }
        public CustomThemeBackground? Background { get; set; }
        public CustomThemeIcon? Icon { get; set; }
        public CustomThemeAnimation? Animation { get; set; }

        // 可选，最多 2 个（CustomThemeValidator.MaxLayers）。icon/animation 是"主图标"，固定贴在歌词框
        // 上方那条 50px 的装饰栏（跟内置皮肤那一整排图标位置一样）；layers 是额外叠加的小装饰，各自贴在
        // 卡片四个角上的一个（anchor），不是替换主图标，是在它之上再加。留空/不填完全不影响主图标那一套——
        // 这是这个字段加进来之前唯一的行为，老主题不用改。见 CustomThemeLayer。
        public List<CustomThemeLayer>? Layers { get; set; }
    }

    /// <summary>一个额外装饰层：自己的图标 + 动画 + 贴在卡片哪个角。跟 CustomTheme 顶层的 icon/animation
    /// 是同一套字段形状（复用 CustomThemeIcon/CustomThemeAnimation），只是多了个 Anchor，也不会
    /// 出现"飘过/飘落整张卡片"那种主图标才有的专属轨道——drift/fall 在这里退化成原地小幅摆动，
    /// 具体差异见 MainWindow.Skins.cs 的 StartLayerAnimation。</summary>
    public sealed class CustomThemeLayer
    {
        public string? Anchor { get; set; } // "top-left" / "top-right" / "bottom-left" / "bottom-right"
        public CustomThemeIcon? Icon { get; set; }
        public CustomThemeAnimation? Animation { get; set; }
    }

    public sealed class CustomThemeColors
    {
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Accent { get; set; }
        public string? Lyric { get; set; }
        public string? Glow { get; set; }
        public double GlowBlur { get; set; } = 3;
        public string? LyricBoxBg { get; set; }
        public string? LyricBoxBorder { get; set; }
    }

    public sealed class CustomThemeBackground
    {
        public string? Type { get; set; }        // "solid" | "gradient"
        public string? Direction { get; set; }    // "vertical" | "diagonal"，只有 gradient 用得上
        public List<string>? Stops { get; set; }  // 2~4 个十六进制颜色
    }

    public sealed class CustomThemeIcon
    {
        public Dictionary<string, string>? Palette { get; set; } // 单字符 -> 十六进制颜色，"." 固定是透明，不用填
        public List<string>? Rows { get; set; }                   // 4~16 行，每行必须等宽，宽度也是 4~16（不强制正方形，见 CustomThemeValidator.MinIconSize/MaxIconSize）
    }

    public sealed class CustomThemeAnimation
    {
        public string? Type { get; set; }  // pulse / twinkle / drift / fall / bob / sway / spin / flicker
        public double? Duration { get; set; } // 秒，不填就用每种招式自己的默认值

        // 可选，不填默认 false。true 的话，不管上面 Type 选的是 8 种招式里的哪一种，这个动画的播放速度
        // 都会跟着系统正在播的音乐响度/鼓点实时变化——跟内置皮肤（黑胶转速、Minecraft 走路变速……）
        // 用的是同一套机制，见 MainWindow.SkinInteractions.cs 的 UpdateMusicReactiveSkin。
        // 受设置页"皮肤音乐律动"这个总开关控制，那个开关关了的话这里勾了也不会生效。
        public bool MusicReactive { get; set; }

        // 可选，"low" / "medium" / "high"，不填默认 medium（等同 1.0 倍，也是这个字段加进来之前
        // 唯一的行为，老主题不用改就还是原来的速度感）。跟内置皮肤共用的"响度/鼓点算出来的播放速度倍率"
        // 是同一份数据，这个字段只决定这份主题自己的动画对那份数据"放大/缩小多少反应"——low 声音大变化
        // 也比较克制，high 反过来会被小声音放得更明显。具体倍数见 CustomThemeValidator.SensitivityToMultiplier，
        // MusicReactive 为 false 的时候这个字段不生效（没有反应可放大/缩小）。
        public string? Sensitivity { get; set; }
    }

    /// <summary>
    /// 校验一份 <see cref="CustomTheme"/>：哪一项没填、哪一项格式不对，都给出具体到字段的错误信息，
    /// 而不是笼统地说"格式错误"——用户是自己手写 JSON，含糊的报错基本等于没用。
    /// </summary>
    public static class CustomThemeValidator
    {
        public static readonly string[] ValidAnimationTypes = { "pulse", "twinkle", "drift", "fall", "bob", "sway", "spin", "flicker" };
        public static readonly string[] ValidSensitivities = { "low", "medium", "high" };

        // 图标网格的行数/每行宽度都必须落在这个范围内，两个方向各自独立判断，不强制正方形——内置皮肤里
        // Steve 就是 8 列 x 16 行的长条形，同一套渲染逻辑（PixelArt.Build）本来就不挑尺寸，渲染这边到
        // 64x64 完全没有技术上的天花板。这两个数纯粹是校验层面的软上限，不是系统限制：MaxIconSize 定这么高
        // 是留给愿意手写更精细图标的人；Mini 小方块/装饰动画那几个展示框（见 MainWindow.xaml 里几个
        // Custom*Icon）还是 20~52px 上下，网格比这个大很多的话，多出来的细节缩小显示时会被压掉看不出来，
        // 纯粹是"画了但看不见"，不是不能用。MinIconSize 保底 4，最省事的 8x8 也一直落在这个范围内。
        public const int MinIconSize = 4;
        public const int MaxIconSize = 64;

        // 额外装饰层：最多 2 个，贴在卡片四个角之一。上限故意压得比 5 个已存主题的上限低很多——
        // 层数一多，渲染开销（每层一份独立的 BuildCustomIcon + 一套动画）线性往上涨，2 个已经够表达
        // "主图标 + 一两个点缀"这种常见组合（内置皮肤里最多的樱花/都市夜景也就 2 个动态元素），
        // 真需要更复杂的场景，本来就更适合做成新的内置皮肤，不是客制化主题这条轻量路径该扛的
        public const int MaxLayers = 2;
        public static readonly string[] ValidAnchors = { "top-left", "top-right", "bottom-left", "bottom-right" };

        public static (CustomTheme? Theme, List<string> Errors) ParseAndValidate(string json)
        {
            var errors = new List<string>();
            CustomTheme? theme;

            try
            {
                // 先按严格模式解析：JSON 本身写错了（少个逗号、引号没配对之类）直接在这一步报出来，
                // 比丢给业务校验逻辑之后报一堆"字段缺失"要清楚得多
                theme = JsonConvert.DeserializeObject<CustomTheme>(json, new JsonSerializerSettings
                {
                    MissingMemberHandling = MissingMemberHandling.Ignore,
                });
            }
            catch (JsonException ex)
            {
                errors.Add($"JSON 格式本身有问题，检查一下有没有漏逗号/少引号/括号没配对：{ex.Message}");
                return (null, errors);
            }

            if (theme == null)
            {
                errors.Add("解析出来是空的，确认粘贴的内容不是空白。");
                return (null, errors);
            }

            if (string.IsNullOrWhiteSpace(theme.Name))
            {
                errors.Add("\"name\" 不能为空——这是要显示在主题选择器里的名字。");
            }

            ValidateColor(theme.Colors?.Title, "colors.title", errors);
            ValidateColor(theme.Colors?.Artist, "colors.artist", errors);
            ValidateColor(theme.Colors?.Accent, "colors.accent", errors);
            ValidateColor(theme.Colors?.Lyric, "colors.lyric", errors);
            ValidateColor(theme.Colors?.Glow, "colors.glow", errors, allowEmpty: true);
            ValidateColor(theme.Colors?.LyricBoxBg, "colors.lyricBoxBg", errors);
            ValidateColor(theme.Colors?.LyricBoxBorder, "colors.lyricBoxBorder", errors);

            if (theme.Background == null)
            {
                errors.Add("\"background\" 整块都没填。");
            }
            else
            {
                string? bgType = theme.Background.Type?.ToLowerInvariant();
                if (bgType != "solid" && bgType != "gradient")
                {
                    errors.Add("\"background.type\" 只能是 \"solid\" 或 \"gradient\"。");
                }

                if (theme.Background.Stops == null || theme.Background.Stops.Count == 0)
                {
                    errors.Add("\"background.stops\" 至少要填 1 个颜色（gradient 建议 2~4 个）。");
                }
                else
                {
                    if (bgType == "gradient" && theme.Background.Stops.Count < 2)
                    {
                        errors.Add("\"background.type\" 是 gradient 的话，\"background.stops\" 至少要 2 个颜色才有渐变效果。");
                    }
                    for (int i = 0; i < theme.Background.Stops.Count; i++)
                    {
                        ValidateColor(theme.Background.Stops[i], $"background.stops[{i}]", errors);
                    }
                }
            }

            if (theme.Icon == null)
            {
                errors.Add("\"icon\" 整块都没填——Mini 小方块和皮肤装饰用的像素图标。");
            }
            else
            {
                ValidateIcon(theme.Icon, errors);
            }

            ValidateAnimation(theme.Animation, "animation", errors);

            if (theme.Layers != null)
            {
                if (theme.Layers.Count > MaxLayers)
                {
                    errors.Add($"\"layers\" 最多只能有 {MaxLayers} 个，现在是 {theme.Layers.Count} 个。");
                }
                for (int i = 0; i < theme.Layers.Count; i++)
                {
                    var layer = theme.Layers[i];
                    string prefix = $"layers[{i}]";

                    string? anchor = layer.Anchor;
                    if (string.IsNullOrWhiteSpace(anchor))
                    {
                        errors.Add($"\"{prefix}.anchor\" 没填，必须是这几种之一：" + string.Join(" / ", ValidAnchors));
                    }
                    else if (!ValidAnchors.Contains(anchor.ToLowerInvariant()))
                    {
                        errors.Add($"\"{prefix}.anchor\" 填的是 \"{anchor}\"，只能是：" + string.Join(" / ", ValidAnchors));
                    }

                    if (layer.Icon == null)
                    {
                        errors.Add($"\"{prefix}.icon\" 整块都没填。");
                    }
                    else
                    {
                        ValidateIcon(layer.Icon, errors, prefix);
                    }

                    ValidateAnimation(layer.Animation, $"{prefix}.animation", errors);
                }
            }

            return (errors.Count == 0 ? theme : null, errors);
        }

        // 主图标的 animation 和每个 layers[i].animation 是同一套校验规则（type 八选一 / duration 是正数 /
        // sensitivity 三档之一），抽出来共用一份，不然多层加进来之后同一段逻辑要复制 MaxLayers+1 遍
        private static void ValidateAnimation(CustomThemeAnimation? animation, string fieldPrefix, List<string> errors)
        {
            if (animation == null || string.IsNullOrWhiteSpace(animation.Type))
            {
                errors.Add($"\"{fieldPrefix}.type\" 没填，必须是这几种之一：" + string.Join(" / ", ValidAnimationTypes));
            }
            else if (!ValidAnimationTypes.Contains(animation.Type.ToLowerInvariant()))
            {
                errors.Add($"\"{fieldPrefix}.type\" 填的是 \"{animation.Type}\"，不认识这个招式，只能是：" + string.Join(" / ", ValidAnimationTypes));
            }

            // duration 不填就用默认值，但填了的话必须是正数——0 或负数会让 WPF 的动画系统在播放时直接抛异常崩溃
            if (animation?.Duration is double d && d <= 0)
            {
                errors.Add($"\"{fieldPrefix}.duration\" 填的是 {d}，必须是大于 0 的数字（不填就用默认值）。");
            }

            // sensitivity 不填就是 medium，填了的话必须是三档之一——大小写不敏感（跟 type 一样）
            string? sensitivity = animation?.Sensitivity;
            if (!string.IsNullOrWhiteSpace(sensitivity) && !ValidSensitivities.Contains(sensitivity.ToLowerInvariant()))
            {
                errors.Add($"\"{fieldPrefix}.sensitivity\" 填的是 \"{sensitivity}\"，只能是：" + string.Join(" / ", ValidSensitivities) + "（不填就是 medium）。");
            }
        }

        private static void ValidateColor(string? hex, string fieldName, List<string> errors, bool allowEmpty = false)
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                if (!allowEmpty) errors.Add($"\"{fieldName}\" 没填。");
                return;
            }
            if (!TryParseHexColor(hex, out _))
            {
                errors.Add($"\"{fieldName}\" 的值 \"{hex}\" 不是合法的十六进制颜色，格式要是 #RRGGBB 或 #AARRGGBB。");
            }
        }

        // fieldPrefix 默认 "icon"（主图标），layers[i].icon 传 "layers[i]" 进来会拼成 "layers[i].icon.rows"
        // 这样的报错——同一套规则，只是报错信息里要能分清是主图标还是第几个额外层出的问题
        private static void ValidateIcon(CustomThemeIcon icon, List<string> errors, string fieldPrefix = "icon")
        {
            string rowsField = fieldPrefix == "icon" ? "icon.rows" : $"{fieldPrefix}.icon.rows";
            string paletteField = fieldPrefix == "icon" ? "icon.palette" : $"{fieldPrefix}.icon.palette";

            if (icon.Rows == null || icon.Rows.Count < MinIconSize || icon.Rows.Count > MaxIconSize)
            {
                errors.Add($"\"{rowsField}\" 必须是 {MinIconSize}~{MaxIconSize} 行，现在是 {icon.Rows?.Count ?? 0} 行。");
                return;
            }

            int width = icon.Rows[0].Length;
            if (width < MinIconSize || width > MaxIconSize)
            {
                errors.Add($"\"{rowsField}\" 每一行长度必须是 {MinIconSize}~{MaxIconSize} 个字符，第 1 行现在是 {width} 个。不要求正方形，行数和每行宽度可以不一样，比如 8 行 x 16 列这种长条形也行。");
                return;
            }

            for (int i = 0; i < icon.Rows.Count; i++)
            {
                if (icon.Rows[i].Length != width)
                {
                    errors.Add($"\"{rowsField}\" 第 {i + 1} 行长度是 {icon.Rows[i].Length}，跟第 1 行的 {width} 不一致——每一行必须一样宽，不然画出来的图会错位。");
                }
            }

            // "icon.palette" 必须存在——就算图标全是 "." 空白格用不上任何颜色，也留一个空对象 {}。
            // 之前这里漏填会直接放行（下面 usedChars 为空就不会报错），结果渲染那边对 Palette 做 .ToDictionary(...)
            // 直接空引用崩掉；现在强制要求这个字段一定存在，从源头堵死这个情况。
            if (icon.Palette == null)
            {
                errors.Add($"\"{paletteField}\" 没填——就算图标不需要颜色，也要留一个空对象 {{}}。");
                return;
            }

            // 键必须正好 1 个字符，不然渲染那边取 key[0] 会因为空字符串直接抛异常崩溃
            foreach (var key in icon.Palette.Keys)
            {
                if (key.Length != 1)
                {
                    errors.Add($"\"{paletteField}\" 里的键 \"{key}\" 必须正好是 1 个字符（不能是空字符串或者多个字符）。");
                }
            }

            var usedChars = icon.Rows.SelectMany(r => r).Distinct().Where(c => c != '.');
            foreach (var c in usedChars)
            {
                string key = c.ToString();
                if (!icon.Palette.TryGetValue(key, out var hex))
                {
                    errors.Add($"\"{rowsField}\" 里用了字符 '{c}'，但 \"{paletteField}\" 里没有给它配颜色。");
                }
                else if (!TryParseHexColor(hex, out _))
                {
                    errors.Add($"\"{paletteField}\" 里 '{c}' 对应的颜色 \"{hex}\" 不是合法的十六进制颜色。");
                }
            }
        }

        /// <summary>把 icon.palette（单字符 -> 十六进制颜色字符串）转成 PixelArt.Build 直接吃得下的
        /// Dictionary&lt;char, Color&gt;，"." 没显式配色的话补一个透明——渲染现场（MainWindow.Skins.cs）和
        /// 编辑页的实时预览（CustomThemeWindow.xaml.cs）都要做这同一步转换，抽出来共用一份，不然两边
        /// 各写一遍，以后调色逻辑（比如要支持简写的 3 位十六进制）改了容易漏改一边。
        /// 调用前应该已经过 ValidateIcon 校验，这里不重复校验，纯粹是格式转换。</summary>
        public static Dictionary<char, Color> BuildIconPalette(CustomThemeIcon icon)
        {
            var palette = icon.Palette!.ToDictionary(
                kv => kv.Key[0],
                kv => { TryParseHexColor(kv.Value, out var c); return c; });
            if (!palette.ContainsKey('.')) palette['.'] = Colors.Transparent;
            return palette;
        }

        // low/high 相对 1.0（medium）不是对称的——1.6 比 1/0.6≈1.67 略保守一点，是刻意的：
        // "反应更明显"这个方向观感上比"更克制"更容易一不小心就晃得太夸张，high 稍微收着点选
        private const double LowSensitivityMultiplier = 0.6;
        private const double MediumSensitivityMultiplier = 1.0;
        private const double HighSensitivityMultiplier = 1.6;

        /// <summary>把 animation.sensitivity 的字符串档位换算成 UpdateMusicReactiveSkin 用的倍率——
        /// 没填/填了不认识的值（理论上校验已经拦过，这里再兜底）都当 medium=1.0，也就是这个字段
        /// 加进来之前唯一的行为，保证老主题不用改就是原来的速度感。</summary>
        public static double SensitivityToMultiplier(string? sensitivity) => sensitivity?.ToLowerInvariant() switch
        {
            "low" => LowSensitivityMultiplier,
            "high" => HighSensitivityMultiplier,
            _ => MediumSensitivityMultiplier,
        };

        /// <summary>#RRGGBB 或 #AARRGGBB，缺 alpha 就当完全不透明。公开给渲染那边直接复用，不用再解析一遍。</summary>
        public static bool TryParseHexColor(string hex, out Color color)
        {
            color = Colors.Transparent;
            if (string.IsNullOrWhiteSpace(hex)) return false;

            string s = hex.Trim().TrimStart('#');
            try
            {
                if (s.Length == 6)
                {
                    byte r = Convert.ToByte(s.Substring(0, 2), 16);
                    byte g = Convert.ToByte(s.Substring(2, 2), 16);
                    byte b = Convert.ToByte(s.Substring(4, 2), 16);
                    color = Color.FromRgb(r, g, b);
                    return true;
                }
                if (s.Length == 8)
                {
                    byte a = Convert.ToByte(s.Substring(0, 2), 16);
                    byte r = Convert.ToByte(s.Substring(2, 2), 16);
                    byte g = Convert.ToByte(s.Substring(4, 2), 16);
                    byte b = Convert.ToByte(s.Substring(6, 2), 16);
                    color = Color.FromArgb(a, r, g, b);
                    return true;
                }
            }
            catch (FormatException)
            {
                return false;
            }
            return false;
        }
    }
}
