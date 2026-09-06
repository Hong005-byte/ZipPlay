namespace PixelLyric8BitFix
{
    /// <summary>一份像素图标的数据——跟客制化主题 icon.rows/icon.palette 是完全同一个形状（单字符网格 +
    /// 单字符->颜色的调色板），渲染成实际位图不在这，各平台自己的图形 API 不一样（Android 端见
    /// PixelLyric8Bit.Mobile 的 MobilePixelIconRenderer，跟 Uno 那边的 PixelIconRenderer.cs 是同一个
    /// 数据源、不同的渲染代码，参照 CustomThemeColorInterop 的说明）。</summary>
    public readonly record struct MobileSkinIcon(string[] Rows, IReadOnlyDictionary<char, RgbaColor> Palette);

    /// <summary>
    /// Mobile 悬浮窗用的皮肤图标表——桌面版 21 套内置皮肤（除了 Custom）的 Mini 图标全部有对应条目，
    /// 图形数据（字符网格 + 调色板）直接从 PixelLyric8BitFix/PixelArt.cs 里对应皮肤的 Create*Icon()
    /// 方法抄出来，不是重新画的：那些方法本来就是"rows + palette 喂给 Build()"这个形状，抄的是
    /// 同一份数据，保证图标形状跟桌面版一模一样，不是"看着差不多"。
    ///
    /// 跟 MobileSkinPalette 是两张独立的表——一个管配色、一个管图标形状，皮肤 Id 是两边对齐的
    /// 唯一线索（都用跟桌面版 PlayerSkin 枚举成员一样的名字）。没有把两者合并成一张表，是因为
    /// 配色（MobileSkinPalette）从阶段 2 就在用、已经稳定跑了好几轮，图标是阶段 6 新加的，拆开
    /// 两张表改动面更小，不用碰已经验证过的那份。
    /// </summary>
    public static class MobileSkinIconCatalog
    {
        private static readonly Dictionary<char, RgbaColor> NotePalette = new()
        {
            ['.'] = RgbaColor.Transparent,
            ['#'] = new RgbaColor(0xFF, 0x8A, 0xB4, 0xF8),
            ['o'] = new RgbaColor(0xFF, 0xFF, 0xFF, 0xFF),
        };
        private static readonly string[] NoteRows =
        {
            "...##...", "...##...", "...##...", "...##o..",
            "..###...", ".#####..", ".#o###..", "..###...",
        };

        private static readonly Dictionary<char, RgbaColor> CrtPalette = new()
        {
            ['.'] = RgbaColor.Transparent,
            ['B'] = new RgbaColor(0xFF, 0x22, 0x22, 0x22),
            ['G'] = new RgbaColor(0xFF, 0x33, 0xFF, 0x66),
            ['g'] = new RgbaColor(0xFF, 0x1A, 0x66, 0x33),
        };
        private static readonly string[] CrtRows =
        {
            "BBBBBBBB", "BGGGGGGB", "BGgGgGgB", "BGGGGGGB",
            "BGgGgGgB", "BGGGGGGB", "BBBBBBBB", "..BB....",
        };

        private static readonly Dictionary<char, RgbaColor> BoltPalette = new()
        {
            ['.'] = RgbaColor.Transparent,
            ['P'] = new RgbaColor(0xFF, 0xFF, 0x2E, 0xD1),
            ['C'] = new RgbaColor(0xFF, 0x00, 0xF0, 0xFF),
        };
        private static readonly string[] BoltRows =
        {
            "...CP...", "..CPP...", ".CPP....", "CPPPP...",
            ".CPP....", "..PPC...", ".PPC....", "PPC.....",
        };

        private static readonly Dictionary<char, RgbaColor> VinylPalette = new()
        {
            ['.'] = RgbaColor.Transparent,
            ['B'] = new RgbaColor(0xFF, 0x14, 0x10, 0x0C),
            ['G'] = new RgbaColor(0xFF, 0xC9, 0xA2, 0x27),
        };
        private static readonly string[] VinylRows =
        {
            "..BBBB..", ".BBBBBB.", "BBBBBBBB", "BBBGGBBB",
            "BBBGGBBB", "BBBBBBBB", ".BBBBBB.", "..BBBB..",
        };

        private static readonly Dictionary<char, RgbaColor> GemPalette = new()
        {
            ['.'] = RgbaColor.Transparent,
            ['C'] = new RgbaColor(0xFF, 0xD8, 0xF0, 0xFF),
            ['W'] = new RgbaColor(0xFF, 0xFF, 0xFF, 0xFF),
        };
        private static readonly string[] GemRows =
        {
            "..CCCC..", ".CCWWCC.", "CCCWWCCC", "CCCCCCCC",
            ".CCCCCC.", "..CCCC..", "...CC...", "........",
        };

        private static readonly Dictionary<char, RgbaColor> CoffeeCupPalette = new()
        {
            ['.'] = RgbaColor.Transparent,
            ['C'] = new RgbaColor(0xFF, 0xE8, 0xD9, 0xC0),
            ['D'] = new RgbaColor(0xFF, 0x4A, 0x2F, 0x1E),
        };
        private static readonly string[] CoffeeCupRows =
        {
            "........", ".CCCCCC.", ".CDDDDC.", ".CDDDDC.",
            ".CDDDDC.", ".CCCCCC.", "..CCCC..", "........",
        };

        private static readonly Dictionary<char, RgbaColor> MoonPalette = new()
        {
            ['.'] = RgbaColor.Transparent,
            ['M'] = new RgbaColor(0xFF, 0xE4, 0xF9, 0xF5),
            ['*'] = new RgbaColor(0xFF, 0x4F, 0xD8, 0xC4),
        };
        private static readonly string[] MoonRows =
        {
            "..MMMM..", ".MMMMMM.", "MMMMMMMM", "MMMMMMMM",
            "MMMMMMMM", ".MMMMMM.", "..MMMM..", "..*...*.",
        };

        private static readonly Dictionary<char, RgbaColor> RaindropPalette = new()
        {
            ['.'] = RgbaColor.Transparent,
            ['#'] = new RgbaColor(0xFF, 0xA8, 0xC5, 0xD6),
            ['h'] = new RgbaColor(0xFF, 0xE8, 0xF4, 0xFA),
        };
        private static readonly string[] RaindropRows =
        {
            "...##...", "..#h##..", ".######.", "########",
            "########", ".######.", "..####..", "...##...",
        };

        private static readonly Dictionary<char, RgbaColor> SparklePalette = new()
        {
            ['.'] = RgbaColor.Transparent,
            ['#'] = new RgbaColor(0xFF, 0xC9, 0xD6, 0xFF),
        };
        private static readonly string[] SparkleRows =
        {
            ".#.##.#.", "..####..", ".######.", "########",
            ".######.", "..####..", ".#.##.#.", "........",
        };

        private static readonly Dictionary<char, RgbaColor> CampfirePalette = new()
        {
            ['.'] = RgbaColor.Transparent,
            ['o'] = new RgbaColor(0xFF, 0xE8, 0x63, 0x0F),
            ['y'] = new RgbaColor(0xFF, 0xFF, 0xD2, 0x3F),
            ['L'] = new RgbaColor(0xFF, 0x5A, 0x3C, 0x22),
        };
        private static readonly string[] CampfireRows =
        {
            "...oo...", "..oyyo..", ".oyyyyo.", ".oyyyyo.",
            "..oyyo..", "...LL...", "..LLLL..", ".L.LL.L.",
        };

        private static readonly Dictionary<char, RgbaColor> BlossomPalette = new()
        {
            ['.'] = RgbaColor.Transparent,
            ['P'] = new RgbaColor(0xFF, 0xF7, 0xA8, 0xC4),
            ['K'] = new RgbaColor(0xFF, 0xFF, 0xDF, 0xA0),
        };
        private static readonly string[] BlossomRows =
        {
            "........", "..P..P..", ".PPPPPP.", "PPPKKPPP",
            "PPPKKPPP", ".PPPPPP.", "..P..P..", "........",
        };

        private static readonly Dictionary<char, RgbaColor> CassettePalette = new()
        {
            ['.'] = RgbaColor.Transparent,
            ['#'] = new RgbaColor(0xFF, 0x3A, 0x2E, 0x22),
            ['T'] = new RgbaColor(0xFF, 0xF0, 0xE6, 0xD2),
            ['O'] = new RgbaColor(0xFF, 0xC1, 0x44, 0x0E),
        };
        private static readonly string[] CassetteRows =
        {
            "########", "#T####T#", "#TOOOOT#", "#TO##OT#",
            "#TOOOOT#", "#T####T#", "########", "........",
        };

        private static readonly Dictionary<char, RgbaColor> CloudPalette = new()
        {
            ['.'] = RgbaColor.Transparent,
            ['#'] = new RgbaColor(0xFF, 0xFF, 0xFF, 0xFF),
            ['c'] = new RgbaColor(0xFF, 0xC8, 0xE6, 0xFF),
        };
        private static readonly string[] CloudRows =
        {
            "........", "..##.##.", ".#######", "########",
            "cccccccc", "........", "........", "........",
        };

        private static readonly Dictionary<char, RgbaColor> CandlePalette = new()
        {
            ['.'] = RgbaColor.Transparent,
            ['o'] = new RgbaColor(0xFF, 0xE8, 0x63, 0x0F),
            ['y'] = new RgbaColor(0xFF, 0xFF, 0xD2, 0x3F),
            ['W'] = new RgbaColor(0xFF, 0xF0, 0xE6, 0xC8),
        };
        private static readonly string[] CandleRows =
        {
            "...oo...", "..oyyo..", "..oyyo..", "...oo...",
            "..WWWW..", "..WWWW..", "..WWWW..", "..WWWW..",
        };

        private static readonly Dictionary<char, RgbaColor> PlantPalette = new()
        {
            ['.'] = RgbaColor.Transparent,
            ['l'] = new RgbaColor(0xFF, 0x5E, 0x86, 0x4E),
            ['L'] = new RgbaColor(0xFF, 0x8F, 0xBC, 0x7A),
            ['P'] = new RgbaColor(0xFF, 0xB5, 0x65, 0x2F),
            ['p'] = new RgbaColor(0xFF, 0x8F, 0x4A, 0x22),
        };
        private static readonly string[] PlantRows =
        {
            "..l..L..", ".LlLLlL.", "..LlLl..", "...LL...",
            "..PPPP..", ".PppppP.", ".PPPPPP.", "........",
        };

        private static readonly Dictionary<char, RgbaColor> SunsetPalette = new()
        {
            ['.'] = RgbaColor.Transparent,
            ['#'] = new RgbaColor(0xFF, 0xF9, 0xC7, 0x84),
            ['w'] = new RgbaColor(0xFF, 0x4A, 0x3B, 0x78),
            ['R'] = new RgbaColor(0xFF, 0xEA, 0x70, 0x93),
        };
        private static readonly string[] SunsetRows =
        {
            "........", "..####..", ".######.", "########",
            "wwwwwwww", "wRwwRwww", "........", "........",
        };

        private static readonly Dictionary<char, RgbaColor> JoystickPalette = new()
        {
            ['.'] = RgbaColor.Transparent,
            ['J'] = new RgbaColor(0xFF, 0xE8, 0x4A, 0x4A),
            ['B'] = new RgbaColor(0xFF, 0x24, 0x18, 0x32),
        };
        private static readonly string[] JoystickRows =
        {
            "...J....", "..JJJ...", "...B....", "...B....",
            ".BBBBB..", ".BBBBB..", "........", "........",
        };

        private static readonly Dictionary<char, RgbaColor> InvaderPalette = new()
        {
            ['.'] = RgbaColor.Transparent,
            ['X'] = new RgbaColor(0xFF, 0xAE, 0xFF, 0x3A),
        };
        private static readonly string[] InvaderRows =
        {
            "..X....X..", "...X..X...", "..XXXXXX..", ".XX.XX.XX.",
            "XXXXXXXXXX", "X.XXXXXX.X", "X.X....X.X", "..XX..XX..",
        };

        private static readonly Dictionary<char, RgbaColor> TrainPalette = new()
        {
            ['.'] = RgbaColor.Transparent,
            ['M'] = new RgbaColor(0xFF, 0x4A, 0x4A, 0x5E),
            ['W'] = new RgbaColor(0xFF, 0xFF, 0xC9, 0x7A),
            ['O'] = new RgbaColor(0xFF, 0x18, 0x16, 0x1E),
        };
        private static readonly string[] TrainRows =
        {
            ".MMMMMMMMMMMM.", "M.WW.WW.WW.WWM", "M.WW.WW.WW.WWM",
            "MMMMMMMMMMMMMM", ".MMMMMMMMMMMM.", "..OO......OO..",
        };

        private static readonly Dictionary<char, RgbaColor> TreePalette = new()
        {
            ['.'] = RgbaColor.Transparent,
            ['L'] = new RgbaColor(0xFF, 0x5C, 0xB3, 0x3B),
            ['l'] = new RgbaColor(0xFF, 0x3E, 0x7A, 0x28),
            ['T'] = new RgbaColor(0xFF, 0x5A, 0x3C, 0x22),
            ['t'] = new RgbaColor(0xFF, 0x7A, 0x52, 0x30),
        };
        private static readonly string[] TreeRows =
        {
            "..LlLL..", ".LLllLL.", "LlLLLLlL", "LLllLLLL", ".LLLllL.",
            "..LlLL..", "...TT...", "...tT...", "...Tt...", "........",
        };

        private static readonly Dictionary<char, RgbaColor> CrownPalette = new()
        {
            ['.'] = RgbaColor.Transparent,
            ['G'] = new RgbaColor(0xFF, 0xE6, 0xB6, 0x55),
            ['Y'] = new RgbaColor(0xFF, 0xFF, 0xE8, 0xA0),
            ['R'] = new RgbaColor(0xFF, 0xC8, 0x2A, 0x4A),
        };
        private static readonly string[] CrownRows =
        {
            ".G.G.G..", ".GGGGGG.", ".GYGYGG.", ".GRGRGG.",
            ".GGGGGG.", "GGGGGGGG", "GGGGGGGG", ".GGGGGG.",
        };

        public static readonly IReadOnlyDictionary<string, MobileSkinIcon> BySkinId = new Dictionary<string, MobileSkinIcon>
        {
            ["Simple"] = new(NoteRows, NotePalette),
            ["Crt"] = new(CrtRows, CrtPalette),
            ["Cyberpunk"] = new(BoltRows, BoltPalette),
            ["Vinyl"] = new(VinylRows, VinylPalette),
            ["Glass"] = new(GemRows, GemPalette),
            ["Lofi"] = new(CoffeeCupRows, CoffeeCupPalette),
            ["Aurora"] = new(MoonRows, MoonPalette),
            ["Rain"] = new(RaindropRows, RaindropPalette),
            ["Starry"] = new(SparkleRows, SparklePalette),
            ["Campfire"] = new(CampfireRows, CampfirePalette),
            ["Sakura"] = new(BlossomRows, BlossomPalette),
            ["Cassette"] = new(CassetteRows, CassettePalette),
            ["Cloud"] = new(CloudRows, CloudPalette),
            ["Candle"] = new(CandleRows, CandlePalette),
            ["Plant"] = new(PlantRows, PlantPalette),
            ["Sunset"] = new(SunsetRows, SunsetPalette),
            ["Arcade"] = new(JoystickRows, JoystickPalette),
            ["Invaders"] = new(InvaderRows, InvaderPalette),
            ["City"] = new(TrainRows, TrainPalette),
            ["Minecraft"] = new(TreeRows, TreePalette),
            ["Crown"] = new(CrownRows, CrownPalette),
        };

        /// <summary>查不到（比如以后新加了皮肤但漏加图标，或者是 "custom:文件名" 这种压根不在这张表里
        /// 的 id）就返回 null——调用方（悬浮窗）该退回"不显示图标，只显示文字"，不是硬塞一个不对的
        /// 图标凑数，也不是崩掉。</summary>
        public static MobileSkinIcon? Find(string skinId) =>
            BySkinId.TryGetValue(skinId, out var icon) ? icon : null;
    }
}
