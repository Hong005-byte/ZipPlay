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
        public List<string>? Rows { get; set; }                   // 必须是 8 行，每行必须等宽
    }

    public sealed class CustomThemeAnimation
    {
        public string? Type { get; set; }  // pulse / twinkle / drift / sway / spin / flicker
        public double? Duration { get; set; } // 秒，不填就用每种招式自己的默认值
    }

    /// <summary>
    /// 校验一份 <see cref="CustomTheme"/>：哪一项没填、哪一项格式不对，都给出具体到字段的错误信息，
    /// 而不是笼统地说"格式错误"——用户是自己手写 JSON，含糊的报错基本等于没用。
    /// </summary>
    public static class CustomThemeValidator
    {
        public static readonly string[] ValidAnimationTypes = { "pulse", "twinkle", "drift", "sway", "spin", "flicker" };

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

            if (theme.Animation == null || string.IsNullOrWhiteSpace(theme.Animation.Type))
            {
                errors.Add("\"animation.type\" 没填，必须是这几种之一：" + string.Join(" / ", ValidAnimationTypes));
            }
            else if (!ValidAnimationTypes.Contains(theme.Animation.Type.ToLowerInvariant()))
            {
                errors.Add($"\"animation.type\" 填的是 \"{theme.Animation.Type}\"，不认识这个招式，只能是：" + string.Join(" / ", ValidAnimationTypes));
            }

            // duration 不填就用默认值，但填了的话必须是正数——0 或负数会让 WPF 的动画系统在播放时直接抛异常崩溃
            if (theme.Animation?.Duration is double d && d <= 0)
            {
                errors.Add($"\"animation.duration\" 填的是 {d}，必须是大于 0 的数字（不填就用默认值）。");
            }

            return (errors.Count == 0 ? theme : null, errors);
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

        private static void ValidateIcon(CustomThemeIcon icon, List<string> errors)
        {
            if (icon.Rows == null || icon.Rows.Count != 8)
            {
                errors.Add($"\"icon.rows\" 必须正好是 8 行，现在是 {icon.Rows?.Count ?? 0} 行。");
                return;
            }

            int width = icon.Rows[0].Length;
            if (width == 0)
            {
                errors.Add("\"icon.rows\" 每一行不能是空字符串。");
                return;
            }

            for (int i = 0; i < icon.Rows.Count; i++)
            {
                if (icon.Rows[i].Length != width)
                {
                    errors.Add($"\"icon.rows\" 第 {i + 1} 行长度是 {icon.Rows[i].Length}，跟第 1 行的 {width} 不一致——每一行必须一样宽，不然画出来的图会错位。");
                }
            }

            // "icon.palette" 必须存在——就算图标全是 "." 空白格用不上任何颜色，也留一个空对象 {}。
            // 之前这里漏填会直接放行（下面 usedChars 为空就不会报错），结果渲染那边对 Palette 做 .ToDictionary(...)
            // 直接空引用崩掉；现在强制要求这个字段一定存在，从源头堵死这个情况。
            if (icon.Palette == null)
            {
                errors.Add("\"icon.palette\" 没填——就算图标不需要颜色，也要留一个空对象 {}。");
                return;
            }

            // 键必须正好 1 个字符，不然渲染那边取 key[0] 会因为空字符串直接抛异常崩溃
            foreach (var key in icon.Palette.Keys)
            {
                if (key.Length != 1)
                {
                    errors.Add($"\"icon.palette\" 里的键 \"{key}\" 必须正好是 1 个字符（不能是空字符串或者多个字符）。");
                }
            }

            var usedChars = icon.Rows.SelectMany(r => r).Distinct().Where(c => c != '.');
            foreach (var c in usedChars)
            {
                string key = c.ToString();
                if (!icon.Palette.TryGetValue(key, out var hex))
                {
                    errors.Add($"\"icon.rows\" 里用了字符 '{c}'，但 \"icon.palette\" 里没有给它配颜色。");
                }
                else if (!TryParseHexColor(hex, out _))
                {
                    errors.Add($"\"icon.palette\" 里 '{c}' 对应的颜色 \"{hex}\" 不是合法的十六进制颜色。");
                }
            }
        }

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
