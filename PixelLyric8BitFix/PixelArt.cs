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

        // 樱花皮肤专属：跟 CreateTree() 同一个树形轮廓（复用同一套已经调好看的形状），
        // 只是把绿色树叶换成粉白樱花，配色跟这套皮肤别的地方（花瓣、边框）保持一致
        public static BitmapSource CreateSakuraTree()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['P'] = Color.FromRgb(0xF7, 0xA8, 0xC4), // 樱花（亮），跟花瓣飘落用的同一个颜色
                ['p'] = Color.FromRgb(0xD9, 0x88, 0xAE), // 樱花（暗，增加纹理）
                ['T'] = Color.FromRgb(0x5A, 0x3C, 0x22), // 树干（暗）
                ['t'] = Color.FromRgb(0x7A, 0x52, 0x30), // 树干（亮）
            };

            string[] rows =
            {
                "..PpPP..",
                ".PPppPP.",
                "PpPPPPpP",
                "PPppPPPP",
                ".PPPppP.",
                "..PpPP..",
                "...TT...",
                "...tT...",
                "...Tt...",
                "........",
            };

            return Build(rows, palette);
        }

        // 海边黄昏皮肤专属：一艘小帆船的剪影，停在海平面上，帆的颜色用的是这套皮肤边框同一个暖橙色
        public static BitmapSource CreateSailboat()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['S'] = Color.FromRgb(0xF9, 0xC7, 0x84), // 帆（亮），跟 SunsetSkinBg 边框同一个暖橙色
                ['s'] = Color.FromRgb(0xE0, 0xA0, 0x5C), // 帆（暗，增加纹理）
                ['H'] = Color.FromRgb(0x2A, 0x1F, 0x3D), // 船身，深色剪影，衬在暮色天空前才有"逆光剪影"的感觉
            };

            string[] rows =
            {
                "........",
                "...S....",
                "..SS....",
                ".SsS....",
                "SSsS....",
                "..s.....",
                ".HHHHH..",
                "HHHHHHH.",
                "........",
                "........",
            };

            return Build(rows, palette);
        }

        // 云朵漂浮皮肤专属：一只热气球，暖色调（红/橙）跟浅蓝天空背景形成对比，一眼能看清
        public static BitmapSource CreateHotAirBalloon()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['R'] = Color.FromRgb(0xE0, 0x6B, 0x4A), // 气球（亮）
                ['r'] = Color.FromRgb(0xB8, 0x4A, 0x30), // 气球（暗，增加纹理）
                ['B'] = Color.FromRgb(0x5A, 0x3C, 0x22), // 吊篮 + 连接绳
            };

            string[] rows =
            {
                "..RRRR..",
                ".RRrrRR.",
                "RRrRRrRR",
                "RRRrrRRR",
                ".RRrrRR.",
                "..RRRR..",
                "...BB...",
                "...BB...",
                "........",
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

        /// <summary>星空太空风专属：一只小飞碟，飞过夜空。圆顶用发光的青绿色，跟满天星星的冷色调呼应。</summary>
        public static BitmapSource CreateUfo()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['C'] = Color.FromRgb(0xB8, 0xC4, 0xCC), // 圆顶边框，银灰色
                ['g'] = Color.FromRgb(0x7A, 0xF0, 0xC8), // 圆顶玻璃，发光青绿色
                ['S'] = Color.FromRgb(0xD8, 0xDE, 0xE4), // 碟身上部，亮银色
                ['s'] = Color.FromRgb(0x7C, 0x86, 0x92), // 碟身下部，暗银色
                ['o'] = Color.FromRgb(0xE8, 0xFF, 0x8A), // 底部两颗指示灯，荧光黄绿色
            };
            string[] rows =
            {
                "........",
                "..CggC..",
                ".CggggC.",
                "SSSSSSSS",
                ".ssssss.",
                "..o..o..",
                "........",
            };
            return Build(rows, palette);
        }

        /// <summary>复古 CRT 终端风专属：一台老式电视机轮廓，天线 + 木纹机身 + 深色屏幕。
        /// 真正的"画面"是下面歌词卡片本身，这个只是摆在角落的一台复古电视外壳装饰。</summary>
        public static BitmapSource CreateRetroTv()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['A'] = Color.FromRgb(0x3A, 0x3A, 0x3A), // 天线
                ['B'] = Color.FromRgb(0xB5, 0x8B, 0x5C), // 机身，木纹棕色
                ['b'] = Color.FromRgb(0x8F, 0x6A, 0x3E), // 机身暗部/底座
                ['S'] = Color.FromRgb(0x14, 0x1A, 0x22), // 屏幕，深色
                ['g'] = Color.FromRgb(0x4E, 0xE0, 0x7A), // 电源指示灯，绿色
            };
            string[] rows =
            {
                "A......A",
                ".BBBBBB.",
                ".BSSSSB.",
                ".BSSSSB.",
                ".BSSSSB.",
                ".BbbbbB.",
                ".BBggBB.",
                "..bbbb..",
            };
            return Build(rows, palette);
        }

        /// <summary>玻璃拟态风专属：一颗悬浮的水晶，跟这套皮肤本身"裂开又愈合"的质感呼应。</summary>
        public static BitmapSource CreateCrystal()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['W'] = Color.FromRgb(0xF0, 0xF5, 0xFF), // 切面高光
                ['C'] = Color.FromRgb(0xA8, 0xC0, 0xF0), // 水晶主体，亮
                ['c'] = Color.FromRgb(0x74, 0x88, 0xD8), // 水晶主体，暗（增加切面感）
            };
            string[] rows =
            {
                "...WW...",
                "..WCCW..",
                ".WCcccW.",
                "WCccccCW",
                ".WCcccW.",
                "..WCCW..",
                "...WW...",
                "........",
            };
            return Build(rows, palette);
        }

        /// <summary>极光雪夜风专属：一只坐着的小北极狐，白色毛皮配奶油色尾巴，安安静静蹲在角落。</summary>
        public static BitmapSource CreateArcticFox()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['W'] = Color.FromRgb(0xF5, 0xF7, 0xFA), // 毛皮，亮白
                ['w'] = Color.FromRgb(0xD4, 0xDC, 0xE8), // 毛皮暗部，淡蓝灰
                ['T'] = Color.FromRgb(0xE8, 0xC9, 0x9C), // 尾巴，奶油色
                ['N'] = Color.FromRgb(0x2A, 0x2A, 0x30), // 眼睛/鼻子
            };
            string[] rows =
            {
                ".W.W....",
                ".WWW....",
                "WWWWW...",
                "WNWWWTT.",
                ".WWWWTTT",
                "..wwwTT.",
                "..w..w..",
                "........",
            };
            return Build(rows, palette);
        }

        /// <summary>雨夜窗景风专属：一只趴在窗台上的橘猫，暖色调跟窗外冷色调的雨形成对比。
        /// 用的是正面"猫脸"构图（不是侧面剪影）——之前试过侧面剪影/趴姿在这么小的渲染尺寸下
        /// 好几版都没做对（不是糊成一团就是变形），正面对称的脸 + 两只尖耳朵 + 两只眼睛，
        /// 在小尺寸下反而更容易一眼认出"这是猫"，回退到这个更稳妥的版本。</summary>
        public static BitmapSource CreateWindowsillCat()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['C'] = Color.FromRgb(0xD8, 0x8A, 0x4A), // 毛色，橘色
                ['c'] = Color.FromRgb(0xA8, 0x5E, 0x2E), // 毛色暗部（脸颊阴影/尾巴）
                ['N'] = Color.FromRgb(0x1A, 0x1A, 0x1E), // 眼睛
                ['P'] = Color.FromRgb(0xE0, 0x7E, 0x92), // 鼻子，粉色
                ['W'] = Color.FromRgb(0xF5, 0xE8, 0xD0), // 胸口白毛
                ['L'] = Color.FromRgb(0x5A, 0x4A, 0x38), // 窗台，木色
            };
            string[] rows =
            {
                ".C........C.",
                ".CC......CC.",
                "CCC......CCC",
                ".CCCCCCCCCC.",
                "CCCCCCCCCCCC",
                "CCNCCCCCCNCC",
                "CCCCPPCCCCCC",
                "CCCcCCcCCCCC",
                ".CCCCCCCCCC.",
                "..CWWWWWWC..",
                "..CC....CCcc",
                "LLLLLLLLLLLL",
            };
            return Build(rows, palette);
        }

        /// <summary>霓虹赛博朋克风专属：一个悬浮的全息小机器人，青色机身 + 品红色"眼睛"，配色跟这套皮肤的霓虹灯管同一路。</summary>
        public static BitmapSource CreateHoloRobot()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['M'] = Color.FromRgb(0x3A, 0xE8, 0xE0), // 机身框架，青色
                ['E'] = Color.FromRgb(0xFF, 0x3A, 0xC8), // "眼睛"，品红色
                ['T'] = Color.FromRgb(0x3A, 0xE8, 0xE0), // 推进器光点，青色（跟机身同色，呼应"全息"的一体发光感）
            };
            string[] rows =
            {
                ".MMMMMM.",
                "M......M",
                "M.E..E.M",
                "M......M",
                ".MMMMMM.",
                "..M..M..",
                ".T....T.",
            };
            return Build(rows, palette);
        }

        /// <summary>复古街机风专属：一台街机柜——顶部发光招牌、屏幕、控制面板（摇杆+两个按钮）、底座。
        /// 全是方方正正的形状（没有圆润的有机曲线），跟猫那几版翻车的教训一致：这种直上直下的
        /// 造型在小尺寸下最不容易画走样，风险最低。</summary>
        public static BitmapSource CreateArcadeCabinet()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['M'] = Color.FromRgb(0xFF, 0xC9, 0x3A), // 顶部招牌，金黄色发光
                ['B'] = Color.FromRgb(0x24, 0x18, 0x32), // 柜体，深紫黑色
                ['b'] = Color.FromRgb(0x14, 0x0C, 0x1C), // 底座，比柜体更暗
                ['S'] = Color.FromRgb(0x1A, 0x2E, 0x3A), // 屏幕，暗青色（暗示"开着但没在放东西"的待机感）
                ['J'] = Color.FromRgb(0xE8, 0x4A, 0x4A), // 摇杆球，红色
                ['R'] = Color.FromRgb(0xFF, 0xC9, 0x3A), // 两颗按钮，跟招牌同色，呼应
            };
            string[] rows =
            {
                "..MMMMMM..",
                ".BBBBBBBB.",
                ".BSSSSSSB.",
                ".BSSSSSSB.",
                ".BSSSSSSB.",
                ".BBBBBBBB.",
                ".BBJ.RRBB.",
                ".BBBBBBBB.",
                "..bb..bb..",
            };
            return Build(rows, palette);
        }

        /// <summary>复古街机风 Mini 图标：摇杆球 + 底座，比装饰用的整台街机柜简化不少，8x8 小尺寸下够看清就行。</summary>
        public static BitmapSource CreateJoystickIcon()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['J'] = Color.FromRgb(0xE8, 0x4A, 0x4A), // 摇杆球，红色
                ['B'] = Color.FromRgb(0x24, 0x18, 0x32), // 摇杆杆身 + 底座
            };
            string[] rows =
            {
                "...J....",
                "..JJJ...",
                "...B....",
                "...B....",
                ".BBBBB..",
                ".BBBBB..",
                "........",
                "........",
            };
            return Build(rows, palette);
        }

        /// <summary>8-bit 太空侵略者风专属：经典"螃蟹形"小怪物剪影——这个造型本身就是拿方块拼出来的
        /// （不是我硬把一个有机形状塞进方格子），是这批新皮肤里画走样风险最低的一个。装饰行里会摆两只，
        /// 同步做小幅度的横向踏步移动（见 MainWindow.Skins.cs 的 StartInvadersMarch）。
        /// Mini 图标直接复用这张图，不用再单独画一份简化版。</summary>
        public static BitmapSource CreateInvader()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['X'] = Color.FromRgb(0xAE, 0xFF, 0x3A), // 荧光青柠绿，跟 CRT 皮肤的纯绿故意区分开
            };
            string[] rows =
            {
                "..X....X..",
                "...X..X...",
                "..XXXXXX..",
                ".XX.XX.XX.",
                "XXXXXXXXXX",
                "X.XXXXXX.X",
                "X.X....X.X",
                "..XX..XX..",
            };
            return Build(rows, palette);
        }

        /// <summary>城市夜景/地铁风专属：一节亮着灯的地铁车厢，横向驶过整条装饰行。
        /// 车身是矩形 + 车窗是矩形 + 车轮是圆点，全是最基础的几何形状，同样是低风险造型。
        /// Mini 图标直接复用这张图。</summary>
        public static BitmapSource CreateTrain()
        {
            var palette = new Dictionary<char, Color>
            {
                ['.'] = Transparent,
                ['M'] = Color.FromRgb(0x4A, 0x4A, 0x5E), // 车身，冷灰色金属感
                ['W'] = Color.FromRgb(0xFF, 0xC9, 0x7A), // 车窗，暖黄色车厢灯光，跟车身冷色形成对比
                ['O'] = Color.FromRgb(0x18, 0x16, 0x1E), // 车轮
            };
            string[] rows =
            {
                ".MMMMMMMMMMMM.",
                "M.WW.WW.WW.WWM",
                "M.WW.WW.WW.WWM",
                "MMMMMMMMMMMMMM",
                ".MMMMMMMMMMMM.",
                "..OO......OO..",
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
