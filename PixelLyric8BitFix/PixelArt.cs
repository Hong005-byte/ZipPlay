using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 纯代码生成的 8-bit 像素素材，替代原先从 Images/*.png 文件加载贴图的方式。
    /// 每个精灵由一个字符网格 + 调色板描述，运行时用 WriteableBitmap 逐像素画出来，
    /// 再配合 XAML 里的 RenderOptions.BitmapScalingMode="NearestNeighbor" 放大后依然是硬边像素块。
    /// </summary>
    internal static class PixelArt
    {
        private static readonly Color Transparent = Colors.Transparent;

        private static readonly Dictionary<char, Color> StevePalette = new()
        {
            ['.'] = Transparent,
            ['H'] = Color.FromRgb(0x4E, 0x2F, 0x13), // 头发
            ['S'] = Color.FromRgb(0xC6, 0x8A, 0x57), // 皮肤
            ['E'] = Color.FromRgb(0x1A, 0x1A, 0x1A), // 眼睛
            ['C'] = Color.FromRgb(0x29, 0xAB, 0xA4), // 上衣（青色）
            ['P'] = Color.FromRgb(0x3B, 0x4A, 0x9E), // 裤子（蓝色）
            ['B'] = Color.FromRgb(0x2B, 0x23, 0x20), // 靴子
        };

        // 走路循环两帧：一帧双腿岔开、一帧双腿并拢，交替播放就会有"迈步"的感觉，
        // 而不是贴图原地平移看起来像在滑冰。
        public static BitmapSource CreateSteveFrame1()
        {
            string[] rows =
            {
                "........",
                ".HHHHHH.",
                "HHSSSSHH",
                "HSSEESSH",
                ".SSSSSS.",
                "..CCCC..",
                ".CCCCCC.",
                "SCCCCCCS",
                ".CCCCCC.",
                ".CCCCCC.",
                "..PPPP..",
                ".PP..PP.",
                ".PP..PP.",
                ".PP..PP.",
                ".BB..BB.",
                "........",
            };
            return Build(rows, StevePalette);
        }

        public static BitmapSource CreateSteveFrame2()
        {
            string[] rows =
            {
                "........",
                ".HHHHHH.",
                "HHSSSSHH",
                "HSSEESSH",
                ".SSSSSS.",
                "..CCCC..",
                ".CCCCCC.",
                "SCCCCCCS",
                ".CCCCCC.",
                ".CCCCCC.",
                "..PPPP..",
                "..PPPP..",
                "..PPPP..",
                "..PPPP..",
                "..BBBB..",
                "........",
            };
            return Build(rows, StevePalette);
        }

        // 开场动画用的跑步小人：跟 Steve 用同一副骨架，但换成单色剪影（绿色，跟 app 图标呼应），
        // 不画细节是因为开场就那么一两秒，剪影反而比塞满细节的贴图更清楚有力。
        private static readonly Dictionary<char, Color> RunnerPalette = new()
        {
            ['.'] = Transparent,
            ['H'] = Color.FromRgb(0x55, 0xFF, 0x55),
            ['S'] = Color.FromRgb(0x55, 0xFF, 0x55),
            ['E'] = Color.FromRgb(0x0A, 0x2A, 0x0A),
            ['C'] = Color.FromRgb(0x55, 0xFF, 0x55),
            ['P'] = Color.FromRgb(0x2E, 0xB8, 0x2E),
            ['B'] = Color.FromRgb(0x1A, 0x66, 0x1A),
        };

        public static BitmapSource CreateRunnerFrame1()
        {
            string[] rows =
            {
                "........",
                ".HHHHHH.",
                "HHSSSSHH",
                "HSSEESSH",
                ".SSSSSS.",
                "..CCCC..",
                ".CCCCCC.",
                "SCCCCCCS",
                ".CCCCCC.",
                ".CCCCCC.",
                "..PPPP..",
                ".PP..PP.",
                ".PP..PP.",
                ".PP..PP.",
                ".BB..BB.",
                "........",
            };
            return Build(rows, RunnerPalette);
        }

        public static BitmapSource CreateRunnerFrame2()
        {
            string[] rows =
            {
                "........",
                ".HHHHHH.",
                "HHSSSSHH",
                "HSSEESSH",
                ".SSSSSS.",
                "..CCCC..",
                ".CCCCCC.",
                "SCCCCCCS",
                ".CCCCCC.",
                ".CCCCCC.",
                "..PPPP..",
                "..PPPP..",
                "..PPPP..",
                "..PPPP..",
                "..BBBB..",
                "........",
            };
            return Build(rows, RunnerPalette);
        }

        // 5x7 点阵字体，够用的字母就先只画这几个（进场动画只需要拼 "ZIPPLAY"）
        private static readonly Dictionary<char, string[]> PixelFont5x7 = new()
        {
            ['Z'] = new[]
            {
                "#####",
                "....#",
                "...#.",
                "..#..",
                ".#...",
                "#....",
                "#####",
            },
            ['I'] = new[]
            {
                ".###.",
                "..#..",
                "..#..",
                "..#..",
                "..#..",
                "..#..",
                ".###.",
            },
            ['P'] = new[]
            {
                "####.",
                "#...#",
                "#...#",
                "####.",
                "#....",
                "#....",
                "#....",
            },
            ['L'] = new[]
            {
                "#....",
                "#....",
                "#....",
                "#....",
                "#....",
                "#....",
                "#####",
            },
            ['A'] = new[]
            {
                ".###.",
                "#...#",
                "#...#",
                "#####",
                "#...#",
                "#...#",
                "#...#",
            },
            ['Y'] = new[]
            {
                "#...#",
                "#...#",
                ".#.#.",
                "..#..",
                "..#..",
                "..#..",
                "..#..",
            },
        };

        /// <summary>开场动画用的像素字 "ZIPPLAY"，橙金色，用简易 5x7 点阵字体逐字母拼出来。</summary>
        public static BitmapSource CreateZipPlayLogo()
        {
            const string word = "ZIPPLAY";
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['#'] = Color.FromRgb(0xF2, 0xA5, 0x3D), // 橙金色
            };

            const int glyphHeight = 7;
            var rows = new string[glyphHeight];
            for (int r = 0; r < glyphHeight; r++)
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < word.Length; i++)
                {
                    sb.Append(PixelFont5x7[word[i]][r]);
                    if (i < word.Length - 1) sb.Append('.'); // 字母间留 1 像素空隙
                }
                rows[r] = sb.ToString();
            }

            return Build(rows, palette);
        }

        public static BitmapSource CreateTree()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['L'] = Color.FromRgb(0x5C, 0xB3, 0x3B), // 树叶（亮）
                ['l'] = Color.FromRgb(0x3E, 0x7A, 0x28), // 树叶（暗，增加纹理）
                ['T'] = Color.FromRgb(0x5A, 0x3C, 0x22), // 树干（暗）
                ['t'] = Color.FromRgb(0x7A, 0x52, 0x30), // 树干（亮）
            };

            string[] rows =
            {
                "..LlLL..",
                ".LLllLL.",
                "LlLLLLlL",
                "LLllLLLL",
                ".LLLllL.",
                "..LlLL..",
                "...TT...",
                "...tT...",
                "...Tt...",
                "........",
            };

            return Build(rows, palette);
        }

        public static BitmapSource CreateDirtTile()
        {
            var palette = new Dictionary<char, Color>
            {
                ['D'] = Color.FromRgb(0x8B, 0x62, 0x39), // 泥土基础色
                ['d'] = Color.FromRgb(0x6E, 0x4C, 0x2C), // 暗色噪点
                ['L'] = Color.FromRgb(0xA1, 0x7A, 0x4C), // 亮色噪点
            };

            string[] rows =
            {
                "DDdDDDLD",
                "DdDDDLDD",
                "DDDLDDDd",
                "LDDDdDDD",
                "DDdDDDDL",
                "DLDDDdDD",
                "DDDdDDLD",
                "dDLDDDDD",
            };

            return Build(rows, palette);
        }

        /// <summary>Lofi 咖啡馆皮肤用的小咖啡杯图标，配合 XAML 里的矢量热气线一起用。</summary>
        public static BitmapSource CreateCoffeeCup()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['C'] = Color.FromRgb(0xE8, 0xD9, 0xC0), // 杯身（奶油色）
                ['D'] = Color.FromRgb(0x4A, 0x2F, 0x1E), // 咖啡（深棕）
            };

            string[] rows =
            {
                "........",
                ".CCCCCC.",
                ".CDDDDC.",
                ".CDDDDC.",
                ".CDDDDC.",
                ".CCCCCC.",
                "..CCCC..",
                "........",
            };

            return Build(rows, palette);
        }

        /// <summary>篝火皮肤用的小篝火图标，配合 XAML 里的火光闪烁 + 火星飘散动效一起用。</summary>
        public static BitmapSource CreateCampfire()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['o'] = Color.FromRgb(0xE8, 0x63, 0x0F), // 外焰（橙）
                ['y'] = Color.FromRgb(0xFF, 0xD2, 0x3F), // 内焰（黄）
                ['L'] = Color.FromRgb(0x5A, 0x3C, 0x22), // 木柴
            };

            string[] rows =
            {
                "...oo...",
                "..oyyo..",
                ".oyyyyo.",
                ".oyyyyo.",
                "..oyyo..",
                "...LL...",
                "..LLLL..",
                ".L.LL.L.",
            };

            return Build(rows, palette);
        }

        /// <summary>CRT 皮肤用的扫描线贴图：每 3 个像素一条半透明暗线，铺满整个窗口后就是老显示器的感觉。</summary>
        public static BitmapSource CreateScanlineTile()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['#'] = Color.FromArgb(90, 0, 0, 0),
            };
            string[] rows = { ".", ".", "#" };
            return Build(rows, palette);
        }

        // ── Mini 模式小方块用的皮肤图标：每套皮肤一个 8x8 填色像素图标，配色尽量贴皮肤自己的主题色 ──

        /// <summary>简约风：一个填色的八分音符。</summary>
        public static BitmapSource CreateNoteIcon()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['#'] = Color.FromRgb(0x8A, 0xB4, 0xF8),
                ['o'] = Colors.White,
            };
            string[] rows =
            {
                "...##...",
                "...##...",
                "...##...",
                "...##o..",
                "..###...",
                ".#####..",
                ".#o###..",
                "..###...",
            };
            return Build(rows, palette);
        }

        /// <summary>CRT 复古终端风：像素小电视，屏幕带扫描线纹理。</summary>
        public static BitmapSource CreateCrtIcon()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['B'] = Color.FromRgb(0x22, 0x22, 0x22),
                ['G'] = Color.FromRgb(0x33, 0xFF, 0x66),
                ['g'] = Color.FromRgb(0x1A, 0x66, 0x33),
            };
            string[] rows =
            {
                "BBBBBBBB",
                "BGGGGGGB",
                "BGgGgGgB",
                "BGGGGGGB",
                "BGgGgGgB",
                "BGGGGGGB",
                "BBBBBBBB",
                "..BB....",
            };
            return Build(rows, palette);
        }

        /// <summary>赛博朋克风：粉青双色的霓虹闪电。</summary>
        public static BitmapSource CreateBoltIcon()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['P'] = Color.FromRgb(0xFF, 0x2E, 0xD1),
                ['C'] = Color.FromRgb(0x00, 0xF0, 0xFF),
            };
            string[] rows =
            {
                "...CP...",
                "..CPP...",
                ".CPP....",
                "CPPPP...",
                ".CPP....",
                "..PPC...",
                ".PPC....",
                "PPC.....",
            };
            return Build(rows, palette);
        }

        /// <summary>黑胶唱片机风：黑胶唱片 + 金色唱片标签。</summary>
        public static BitmapSource CreateVinylIcon()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['B'] = Color.FromRgb(0x14, 0x10, 0x0C),
                ['G'] = Color.FromRgb(0xC9, 0xA2, 0x27),
            };
            string[] rows =
            {
                "..BBBB..",
                ".BBBBBB.",
                "BBBBBBBB",
                "BBBGGBBB",
                "BBBGGBBB",
                "BBBBBBBB",
                ".BBBBBB.",
                "..BBBB..",
            };
            return Build(rows, palette);
        }

        /// <summary>玻璃拟态风：冰蓝色宝石，带一道白色高光。</summary>
        public static BitmapSource CreateGemIcon()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['C'] = Color.FromRgb(0xD8, 0xF0, 0xFF),
                ['W'] = Colors.White,
            };
            string[] rows =
            {
                "..CCCC..",
                ".CCWWCC.",
                "CCCWWCCC",
                "CCCCCCCC",
                ".CCCCCC.",
                "..CCCC..",
                "...CC...",
                "........",
            };
            return Build(rows, palette);
        }

        /// <summary>极光雪夜风：满月 + 两粒小星星。</summary>
        public static BitmapSource CreateMoonIcon()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['M'] = Color.FromRgb(0xE4, 0xF9, 0xF5),
                ['*'] = Color.FromRgb(0x4F, 0xD8, 0xC4),
            };
            string[] rows =
            {
                "..MMMM..",
                ".MMMMMM.",
                "MMMMMMMM",
                "MMMMMMMM",
                "MMMMMMMM",
                ".MMMMMM.",
                "..MMMM..",
                "..*...*.",
            };
            return Build(rows, palette);
        }

        /// <summary>雨夜窗景风：一滴带高光的水滴。</summary>
        public static BitmapSource CreateRaindropIcon()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['#'] = Color.FromRgb(0xA8, 0xC5, 0xD6),
                ['h'] = Color.FromRgb(0xE8, 0xF4, 0xFA),
            };
            string[] rows =
            {
                "...##...",
                "..#h##..",
                ".######.",
                "########",
                "########",
                ".######.",
                "..####..",
                "...##...",
            };
            return Build(rows, palette);
        }

        /// <summary>星空太空风：四角闪光的星星。</summary>
        public static BitmapSource CreateSparkleIcon()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['#'] = Color.FromRgb(0xC9, 0xD6, 0xFF),
            };
            string[] rows =
            {
                ".#.##.#.",
                "..####..",
                ".######.",
                "########",
                ".######.",
                "..####..",
                ".#.##.#.",
                "........",
            };
            return Build(rows, palette);
        }

        /// <summary>樱花风：粉色四瓣花 + 花蕊。</summary>
        public static BitmapSource CreateBlossomIcon()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['P'] = Color.FromRgb(0xF7, 0xA8, 0xC4),
                ['K'] = Color.FromRgb(0xFF, 0xDF, 0xA0),
            };
            string[] rows =
            {
                "........",
                "..P..P..",
                ".PPPPPP.",
                "PPPKKPPP",
                "PPPKKPPP",
                ".PPPPPP.",
                "..P..P..",
                "........",
            };
            return Build(rows, palette);
        }

        /// <summary>复古磁带机风：磁带机身 + 两个卷盘。</summary>
        public static BitmapSource CreateCassetteIcon()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['#'] = Color.FromRgb(0x3A, 0x2E, 0x22),
                ['T'] = Color.FromRgb(0xF0, 0xE6, 0xD2),
                ['O'] = Color.FromRgb(0xC1, 0x44, 0x0E),
            };
            string[] rows =
            {
                "########",
                "#T####T#",
                "#TOOOOT#",
                "#TO##OT#",
                "#TOOOOT#",
                "#T####T#",
                "########",
                "........",
            };
            return Build(rows, palette);
        }

        /// <summary>云朵漂浮风 Mini 图标：蓬松的双色像素云朵。</summary>
        public static BitmapSource CreateCloudIcon()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['#'] = Colors.White,
                ['c'] = Color.FromRgb(0xC8, 0xE6, 0xFF),
            };
            string[] rows =
            {
                "........",
                "..##.##.",
                ".#######",
                "########",
                "cccccccc",
                "........",
                "........",
                "........",
            };
            return Build(rows, palette);
        }

        /// <summary>烛光冥想风：一根点燃的蜡烛，蜡身填色 + 双色火苗。RowDecor 装饰图和 Mini 图标共用这一份。</summary>
        public static BitmapSource CreateCandle()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['o'] = Color.FromRgb(0xE8, 0x63, 0x0F),
                ['y'] = Color.FromRgb(0xFF, 0xD2, 0x3F),
                ['W'] = Color.FromRgb(0xF0, 0xE6, 0xC8),
            };
            string[] rows =
            {
                "...oo...",
                "..oyyo..",
                "..oyyo..",
                "...oo...",
                "..WWWW..",
                "..WWWW..",
                "..WWWW..",
                "..WWWW..",
            };
            return Build(rows, palette);
        }

        /// <summary>绿植角落风：陶土花盆 + 双色叶片。RowDecor 装饰图和 Mini 图标共用这一份。</summary>
        public static BitmapSource CreatePlant()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['l'] = Color.FromRgb(0x5E, 0x86, 0x4E),
                ['L'] = Color.FromRgb(0x8F, 0xBC, 0x7A),
                ['P'] = Color.FromRgb(0xB5, 0x65, 0x2F),
                ['p'] = Color.FromRgb(0x8F, 0x4A, 0x22),
            };
            string[] rows =
            {
                "..l..L..",
                ".LlLLlL.",
                "..LlLl..",
                "...LL...",
                "..PPPP..",
                ".PppppP.",
                ".PPPPPP.",
                "........",
            };
            return Build(rows, palette);
        }

        /// <summary>海边黄昏风 Mini 图标：夕阳 + 泛着微光的海面。</summary>
        public static BitmapSource CreateSunsetIcon()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['#'] = Color.FromRgb(0xF9, 0xC7, 0x84),
                ['w'] = Color.FromRgb(0x4A, 0x3B, 0x78),
                ['R'] = Color.FromRgb(0xEA, 0x70, 0x93),
            };
            string[] rows =
            {
                "........",
                "..####..",
                ".######.",
                "########",
                "wwwwwwww",
                "wRwwRwww",
                "........",
                "........",
            };
            return Build(rows, palette);
        }

        /// <summary>
        /// 给客制化主题用的公开入口——用户在"自定义主题"页面填的 rows/palette 就是这个 Build 方法
        /// 原本就在吃的格式，不用再单独设计一套客制化专属的画图逻辑。
        /// </summary>
        public static BitmapSource BuildCustomIcon(string[] rows, IReadOnlyDictionary<char, Color> palette) => Build(rows, palette);

        private static BitmapSource Build(string[] rows, IReadOnlyDictionary<char, Color> palette)
        {
            int height = rows.Length;
            int width = rows[0].Length;

            var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            var pixels = new byte[width * height * 4];

            for (int y = 0; y < height; y++)
            {
                string row = rows[y];
                for (int x = 0; x < width; x++)
                {
                    Color color = palette.TryGetValue(row[x], out var c) ? c : Transparent;
                    int i = (y * width + x) * 4;
                    // BGRA32，且要求预乘 alpha；这里的颜色要么全透明要么全不透明，直接写即可。
                    pixels[i + 0] = color.B;
                    pixels[i + 1] = color.G;
                    pixels[i + 2] = color.R;
                    pixels[i + 3] = color.A;
                }
            }

            bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
            bitmap.Freeze();
            return bitmap;
        }
    }
}
