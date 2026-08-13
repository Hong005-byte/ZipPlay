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
