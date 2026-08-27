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

        public static bool TryParseHex(string hex, out Color color) => CustomThemeColorInterop.TryParseHexColor(hex, out color);

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

        /// <summary>多帧版 BuildIcon——一份网格列表（每帧一个，调用方已经保证过全部同尺寸）+ 共用的一份
        /// 字符调色板，转成 CustomThemeIcon。只有 1 帧的话直接产出 rows（不包一层 frames），跟这个
        /// 功能加进来之前"画完只有一张静态图"的输出长得一模一样，不会因为用户压根没碰"帧"这个概念
        /// 就平白多出一层结构；2 帧以上才产出 frames。调色板是所有帧摊平之后一起收集"实际用到的字符"
        /// 算出来的一份，不是每帧各自算——不然帧与帧之间调色板不一致，画出来的字符在另一帧里找不到颜色。</summary>
        public static CustomThemeIcon BuildFrames(IReadOnlyList<char[,]> grids, int width, int height, IReadOnlyDictionary<char, Color> palette)
        {
            var usedChars = new HashSet<char>();
            var frameRows = new List<List<string>>(grids.Count);
            foreach (var grid in grids)
            {
                var rows = new List<string>(height);
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
                frameRows.Add(rows);
            }

            var paletteDict = new Dictionary<string, string>();
            foreach (char c in usedChars.OrderBy(c => c))
            {
                if (palette.TryGetValue(c, out var color))
                {
                    paletteDict[c.ToString()] = ColorToHex(color);
                }
            }

            return frameRows.Count == 1
                ? new CustomThemeIcon { Rows = frameRows[0], Palette = paletteDict }
                : new CustomThemeIcon { Frames = frameRows, Palette = paletteDict };
        }

        /// <summary>TryLoadIcon 的多帧版——有 icon.frames 就摊开全部帧续画，没有就退化成 icon.rows 那唯一
        /// 一帧（结果列表长度 1），跟 BuildFrames 是一体两面。同样不做完整校验，只要求"帧数据本身合理"：
        /// 每帧非空、每帧内部每行等宽、而且所有帧必须彼此同尺寸——最后这条是画板独有的要求（校验器本身
        /// 也这么要求，见 CustomThemeValidator.ValidateFrames），尺寸对不上直接返回 null，不硬凑，
        /// 调用方（IconPainterWindow）当作"没有可续画的"，从空白网格开始。</summary>
        public static (List<char[,]> Grids, int Width, int Height, Dictionary<char, Color> Palette)? TryLoadFrames(CustomThemeIcon icon)
        {
            List<List<string>>? rowSets = icon.Frames is { Count: > 0 } frames
                ? frames
                : (icon.Rows != null ? new List<List<string>> { icon.Rows } : null);
            if (rowSets == null) return null;

            int? width = null, height = null;
            var grids = new List<char[,]>(rowSets.Count);
            foreach (var rows in rowSets)
            {
                if (rows == null || rows.Count == 0) return null;
                int h = rows.Count;
                int w = rows[0].Length;
                if (w == 0 || rows.Any(r => r.Length != w)) return null;

                width ??= w;
                height ??= h;
                if (w != width || h != height) return null; // 帧尺寸彼此不一致，画板要求全部帧同尺寸，不续画

                var grid = new char[h, w];
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        grid[y, x] = rows[y][x];
                    }
                }
                grids.Add(grid);
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

            return (grids, width!.Value, height!.Value, palette);
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

        // ── 画布操作的纯逻辑（桶装填充 / 图片导入量化）──────────────────────────

        /// <summary>桶装填充：从 (startX, startY) 出发，把上下左右四连通、且颜色跟起点一样的格子
        /// 统一改成 newChar——标准 flood fill，直接原地改 grid（char[,] 是引用类型，调用方不需要
        /// 拿返回值重新赋值）。起点颜色本来就等于 newChar 的话直接返回 false，不做无意义的改动/重绘。
        /// 用队列迭代而不是递归，避免大网格（64x64，最多 4096 格）时递归深度太夸张。</summary>
        public static bool FloodFill(char[,] grid, int width, int height, int startX, int startY, char newChar)
        {
            char original = grid[startY, startX];
            if (original == newChar) return false;

            var queue = new Queue<(int x, int y)>();
            queue.Enqueue((startX, startY));
            grid[startY, startX] = newChar;

            void TryEnqueue(int x, int y)
            {
                if (x < 0 || x >= width || y < 0 || y >= height) return;
                if (grid[y, x] != original) return;
                grid[y, x] = newChar;
                queue.Enqueue((x, y));
            }

            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                TryEnqueue(x - 1, y);
                TryEnqueue(x + 1, y);
                TryEnqueue(x, y - 1);
                TryEnqueue(x, y + 1);
            }
            return true;
        }

        /// <summary>把一份已经降采样到目标网格大小的颜色矩阵（null 表示这一格透明，比如导入图片时
        /// 那一块原图本来就是透明/半透明的）量化成不超过 maxColors 种颜色的画板网格——用最简单的
        /// "频率优先 + 就近吸附"：颜色先按 32 一档取整合并掉肉眼几乎分不出差别的相近色，按出现次数
        /// 从高到低最多留下 maxColors 种当独立颜色，其余格子改用这些留下的颜色里欧氏距离最近的一个替代。
        /// 颜色如果跟 existingPalette 里已有的某个颜色完全一样，直接复用那个字符（不占用新名额，
        /// 也不会让画布上原来用这个字符的格子意外变成别的颜色）；需要新字符时，按
        /// NextAvailableChar 的分配顺序来，调用方需要保证 maxColors 不超过"当前还剩多少个字符可分配"，
        /// 这样才能保证这个方法分配出的新字符不会超过调色板 45 种颜色的硬上限。
        /// 返回值里 AssignedColors 只包含"这次新分配出来的颜色"，不包含复用的已有颜色——调用方
        /// 用它来往 _palette 里 merge 新增的部分即可，已有的颜色不需要重新添加一遍。</summary>
        public static (char[,] Grid, Dictionary<char, Color> AssignedColors) QuantizeToGrid(
            Color?[,] pixels, IReadOnlyDictionary<char, Color> existingPalette, int maxColors)
        {
            int height = pixels.GetLength(0);
            int width = pixels.GetLength(1);
            var grid = new char[height, width];

            static Color RoundColor(Color c) => Color.FromRgb((byte)(c.R / 32 * 32), (byte)(c.G / 32 * 32), (byte)(c.B / 32 * 32));

            var rounded = new Color?[height, width];
            var freq = new Dictionary<Color, int>();
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (pixels[y, x] is not { } c) continue;
                    var r = RoundColor(c);
                    rounded[y, x] = r;
                    freq[r] = freq.TryGetValue(r, out int n) ? n + 1 : 1;
                }
            }

            if (freq.Count == 0 || maxColors <= 0)
            {
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        grid[y, x] = '.';
                return (grid, new Dictionary<char, Color>());
            }

            var kept = freq.OrderByDescending(kv => kv.Value).Take(maxColors).Select(kv => kv.Key).ToList();

            var colorToChar = new Dictionary<Color, char>();
            var assigned = new Dictionary<char, Color>();
            var usedChars = new HashSet<char>(existingPalette.Keys);
            foreach (var color in kept)
            {
                // color 是取整合并过的"桶代表色"，existingPalette 里的颜色是原始精确值——两边都按
                // 同一档位取整之后再比较，不然一个 32 一档的四舍五入就会让本该复用的相近色被判定成
                // "跟已有颜色不一样"，平白多分配一个新字符
                char? reuse = null;
                foreach (var (existingChar, existingColor) in existingPalette)
                {
                    if (RoundColor(existingColor) == color) { reuse = existingChar; break; }
                }

                if (reuse != null)
                {
                    colorToChar[color] = reuse.Value;
                    continue;
                }

                char? next = NextAvailableChar(usedChars);
                if (next == null) continue; // 理论上不会发生（调用方保证 maxColors 不超过剩余可分配字符数），
                                             // 万一发生就跳过——这个颜色后面走"就近吸附"逻辑，落到已分配的颜色里最接近的一个
                usedChars.Add(next.Value);
                colorToChar[color] = next.Value;
                assigned[next.Value] = color;
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (rounded[y, x] is not { } r) { grid[y, x] = '.'; continue; }
                    if (colorToChar.TryGetValue(r, out char c)) { grid[y, x] = c; continue; }

                    // 没被留下的颜色（出现次数没排进前 maxColors 名）——吸附到已经分配了字符的颜色里
                    // 欧氏距离最近的一个
                    Color nearest = default;
                    int bestDist = int.MaxValue;
                    foreach (var candidate in colorToChar.Keys)
                    {
                        int dr = candidate.R - r.R, dg = candidate.G - r.G, db = candidate.B - r.B;
                        int dist = dr * dr + dg * dg + db * db;
                        if (dist < bestDist) { bestDist = dist; nearest = candidate; }
                    }
                    grid[y, x] = colorToChar.Count > 0 ? colorToChar[nearest] : '.';
                }
            }

            return (grid, assigned);
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
