using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Media;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// "🖌️ 像素画板"背后的纯逻辑：把画板的网格状态（每格一个字符 + 字符对应的颜色）转成
    /// CustomThemeIcon 能用的 { palette, rows } 形状，反过来也能把已有的 icon JSON 解析回网格状态，
    /// 方便"继续编辑一个已经手写/已经画过的图标"而不是每次都从空白开始。
    /// 不碰 UI（不引用 Window/Grid/Rectangle 这些控件），只处理数据，方便单元测试；
    /// IconPainterWindow 负责"点格子"这件事本身，点完调这里的方法把结果转成 JSON。
    /// </summary>
    internal static class PixelIconEditor
    {
        // 分配给"新颜色"的候选字符池——排掉 "."（固定表示透明）、'"' 和 '\'（JSON 字符串里需要转义，
        // 用作 key 会让生成的 JSON 变得别扭，干脆从候选池里直接排除，不用处理转义）。45 个字符
        // 对任何一个像素图标来说都绰绰有余，用完了（几乎不可能）就没法再加新颜色，UI 那边会挡住。
        public const string AssignableChars = "#owrgbypcmkxzqvnjhtdsluf0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        /// <summary>从当前已用的字符集合里找一个没被占用的候选字符——分配新颜色时用。没有可用的了
        /// 返回 null，调用方（IconPainterWindow）应该提示"颜色种类到上限了"，不是让程序崩溃。</summary>
        public static char? NextAvailableChar(IEnumerable<char> usedChars)
        {
            var used = new HashSet<char>(usedChars);
            foreach (char c in AssignableChars)
            {
                if (!used.Contains(c)) return c;
            }
            return null;
        }

        public static string ColorToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        public static bool TryParseHex(string hex, out Color color) => CustomThemeValidator.TryParseHexColor(hex, out color);

        /// <summary>把画板网格（grid[y,x]，'.' 表示空）+ 字符调色板转成 CustomThemeIcon——只保留网格里
        /// 真的用到的颜色，没画上去的颜色不会被带进 palette，输出干净，不会让人误以为"这个符号有用到"。</summary>
        public static CustomThemeIcon BuildIcon(char[,] grid, int width, int height, IReadOnlyDictionary<char, Color> palette)
        {
            var rows = new List<string>(height);
            var usedChars = new HashSet<char>();
            for (int y = 0; y < height; y++)
            {
                var sb = new StringBuilder(width);
                for (int x = 0; x < width; x++)
                {
                    char c = grid[y, x];
                    sb.Append(c);
                    if (c != '.') usedChars.Add(c);
                }
                rows.Add(sb.ToString());
            }

            var paletteDict = new Dictionary<string, string>();
            foreach (char c in usedChars.OrderBy(c => c))
            {
                if (palette.TryGetValue(c, out var color))
                {
                    paletteDict[c.ToString()] = ColorToHex(color);
                }
            }

            return new CustomThemeIcon { Rows = rows, Palette = paletteDict };
        }

        /// <summary>反过来：把一个 CustomThemeIcon 摊开成画板能直接用的网格 + 调色板，用来"续画"一个
        /// 已经存在的图标。传进来的 icon 不需要先过完整校验——只要求 rows 非空且每行一样宽，
        /// 这个方法本身不对尺寸范围（4~64）做限制，画板 UI 自己决定要不要收窄，宽松一点方便复用。
        /// 解析不出来（行宽不一致、调色板颜色写挂了……）返回 null，调用方应该当作"没有可续画的"，
        /// 从空白网格开始，而不是弹一堆错误吓跑用户——毕竟这只是个辅助工具，不是校验入口。</summary>
        public static (char[,] Grid, int Width, int Height, Dictionary<char, Color> Palette)? TryLoadIcon(CustomThemeIcon icon)
        {
            if (icon.Rows == null || icon.Rows.Count == 0) return null;
            int height = icon.Rows.Count;
            int width = icon.Rows[0].Length;
            if (width == 0) return null;
            if (icon.Rows.Any(r => r.Length != width)) return null;

            var grid = new char[height, width];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    grid[y, x] = icon.Rows[y][x];
                }
            }

            var palette = new Dictionary<char, Color>();
            if (icon.Palette != null)
            {
                foreach (var (key, hex) in icon.Palette)
                {
                    if (key.Length == 1 && TryParseHex(hex, out var color))
                    {
                        palette[key[0]] = color;
                    }
                }
            }

            return (grid, width, height, palette);
        }

        /// <summary>从一份任意 JSON 文本（可能是完整主题、可能是用户还没编辑完的半成品）里找出顶层的
        /// "icon" 字段解析出来——找不到、不是合法 JSON、icon 本身解析失败，统统返回 null，
        /// 调用方（打开画板时）就当没有可续画的东西，从空白网格开始，不弹错误。</summary>
        public static CustomThemeIcon? TryExtractIcon(string themeJson)
        {
            try
            {
                var obj = JObject.Parse(themeJson);
                var iconToken = obj["icon"] ?? obj["Icon"]; // 兼容老文件可能还是 PascalCase（见 CustomThemeStore.Save 的说明）
                return iconToken?.ToObject<CustomThemeIcon>();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>把画好的 icon 替换/新增进当前编辑框的 JSON 里，其它字段原样保留——用于"插入到
        /// 编辑框"这个操作。themeJson 必须是合法 JSON（不需要是完整合法的 CustomTheme，半成品也行，
        /// 只要花括号/引号配对正确），解析失败返回 null，调用方应该退化成"复制到剪贴板让用户自己粘"。</summary>
        public static string? TryInsertIconIntoJson(string themeJson, CustomThemeIcon icon)
        {
            try
            {
                var obj = JObject.Parse(themeJson);
                obj.Remove("Icon"); // 如果原来是老格式的 PascalCase 键，先拿掉，不然会跟新插入的 "icon" 同时存在
                obj["icon"] = JObject.FromObject(icon, JsonSerializer.Create(CustomThemeValidator.SerializerSettings));
                return obj.ToString(Formatting.Indented);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>单独把 icon 序列化成一份可以直接复制粘贴的 JSON 片段（就是 {"palette":…,"rows":…}
        /// 这一整块），"插入"失败时的兜底路径用这个塞进剪贴板。</summary>
        public static string SerializeIconFragment(CustomThemeIcon icon) =>
            JsonConvert.SerializeObject(icon, Formatting.Indented, CustomThemeValidator.SerializerSettings);
    }
}
