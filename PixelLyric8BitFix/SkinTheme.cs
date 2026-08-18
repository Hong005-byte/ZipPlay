using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 每套皮肤的主题定义：字体/主界面配色 + Mini 小方块配色/图标，全部集中在这一个地方。
    /// 以前这份数据分散在 MainWindow 的 ApplySkinPalette 和 ApplyMiniBadgeAppearance 两个独立
    /// switch 里，改一个皮肤的颜色容易只改中一边、导致主界面和 Mini 方块的配色慢慢对不上。
    /// 现在只有这一份 <see cref="For"/>，两边都从这读，不会再有"改了一半漏了另一半"的问题。
    /// </summary>
    internal sealed class SkinTheme
    {
        public required FontFamily Font { get; init; }
        public required Color Title { get; init; }
        public required Color Artist { get; init; }
        public required Color Accent { get; init; }
        public required Color Lyric { get; init; }
        public required Color Glow { get; init; }
        public double GlowBlur { get; init; }
        public required Color LyricBoxBg { get; init; }
        public required Color LyricBoxBorder { get; init; }

        public required Color MiniBg { get; init; }
        public required Color MiniBorder { get; init; }
        public required Func<BitmapSource> MiniIcon { get; init; } // 延迟到真正进 Mini 模式才生成贴图，用不到就不用画

        /// <summary>内置皮肤在选择器里用的"表情符号 + 名字"，跟 SkinPickerWindow.xaml 里 21 张皮肤卡片
        /// 上写的文字一字不差——分享卡片的"卡片风格"下拉框复用这份名字，不用再抄一遍。
        /// PlayerSkin.Custom 不走这个方法：客制化主题的名字是用户自己在 JSON 里写的 Name 字段，
        /// 调用方（ListeningStatsWindow）对 Custom 单独处理，这里给个保底文案不让它崩。</summary>
        public static string GetDisplayName(PlayerSkin skin) => skin switch
        {
            PlayerSkin.Minecraft => "🧱 Minecraft",
            PlayerSkin.Simple => "▬ 简约风",
            PlayerSkin.Crt => "📺 CRT 终端",
            PlayerSkin.Cyberpunk => "🌆 赛博朋克",
            PlayerSkin.Vinyl => "💿 黑胶唱片",
            PlayerSkin.Glass => "🧊 玻璃拟态",
            PlayerSkin.Lofi => "☕ 复古咖啡馆",
            PlayerSkin.Aurora => "🌙 极光雪夜",
            PlayerSkin.Rain => "🌧️ 雨夜窗景",
            PlayerSkin.Starry => "✨ 星空太空",
            PlayerSkin.Campfire => "🏕️ 篝火露营",
            PlayerSkin.Sakura => "🌸 樱花",
            PlayerSkin.Cassette => "📼 复古磁带",
            PlayerSkin.Cloud => "☁️ 云朵漂浮",
            PlayerSkin.Candle => "🕯️ 烛光冥想",
            PlayerSkin.Plant => "🪴 绿植角落",
            PlayerSkin.Sunset => "🌊 海边黄昏",
            PlayerSkin.Arcade => "🕹️ 复古街机",
            PlayerSkin.Invaders => "👾 太空侵略者",
            PlayerSkin.City => "🚇 城市夜景",
            PlayerSkin.Crown => "👑 尊贵皇冠",
            _ => "🎨 自定义主题",
        };

        public static SkinTheme For(PlayerSkin skin) => skin switch
        {
            PlayerSkin.Crt => new SkinTheme
            {
                Font = new FontFamily("Consolas"),
                Title = Color.FromRgb(0x33, 0xFF, 0x66), Artist = Color.FromRgb(0x22, 0xCC, 0x55), Accent = Color.FromRgb(0x33, 0xFF, 0x66),
                Lyric = Color.FromRgb(0x66, 0xFF, 0xAA), Glow = Color.FromRgb(0x33, 0xFF, 0x66), GlowBlur = 6,
                LyricBoxBg = Color.FromArgb(0xCC, 0, 0, 0), LyricBoxBorder = Color.FromRgb(0x1A, 0x66, 0x1A),
                MiniBg = Colors.Black, MiniBorder = Color.FromRgb(0x33, 0xFF, 0x66), MiniIcon = PixelArt.CreateCrtIcon,
            },

            PlayerSkin.Cyberpunk => new SkinTheme
            {
                Font = new FontFamily("Segoe UI Semibold"),
                Title = Color.FromRgb(0xFF, 0x2E, 0xD1), Artist = Color.FromRgb(0x00, 0xF0, 0xFF), Accent = Color.FromRgb(0x00, 0xF0, 0xFF),
                Lyric = Color.FromRgb(0xFF, 0x2E, 0xD1), Glow = Color.FromRgb(0x00, 0xF0, 0xFF), GlowBlur = 10,
                LyricBoxBg = Color.FromArgb(0xB3, 0x12, 0x00, 0x22), LyricBoxBorder = Color.FromRgb(0xFF, 0x2E, 0xD1),
                MiniBg = Color.FromRgb(0x12, 0x00, 0x22), MiniBorder = Color.FromRgb(0xFF, 0x2E, 0xD1), MiniIcon = PixelArt.CreateBoltIcon,
            },

            PlayerSkin.Vinyl => new SkinTheme
            {
                Font = new FontFamily("Georgia"),
                Title = Color.FromRgb(0xF1, 0xE3, 0xC6), Artist = Color.FromRgb(0xC9, 0xA2, 0x27), Accent = Color.FromRgb(0xC9, 0xA2, 0x27),
                Lyric = Color.FromRgb(0xF1, 0xE3, 0xC6), Glow = Colors.Black, GlowBlur = 2,
                LyricBoxBg = Color.FromArgb(0xCC, 0x1A, 0x12, 0x0C), LyricBoxBorder = Color.FromRgb(0xC9, 0xA2, 0x27),
                MiniBg = Color.FromRgb(0x1A, 0x12, 0x0C), MiniBorder = Color.FromRgb(0xC9, 0xA2, 0x27), MiniIcon = PixelArt.CreateVinylIcon,
            },

            PlayerSkin.Glass => new SkinTheme
            {
                Font = new FontFamily("Segoe UI Light"),
                Title = Colors.White, Artist = Color.FromRgb(0xEA, 0xEA, 0xEA), Accent = Colors.White,
                Lyric = Colors.White, Glow = Colors.Black, GlowBlur = 4,
                LyricBoxBg = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF), LyricBoxBorder = Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF),
                MiniBg = Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF), MiniBorder = Colors.White, MiniIcon = PixelArt.CreateGemIcon,
            },

            PlayerSkin.Simple => new SkinTheme
            {
                Font = new FontFamily("Segoe UI"),
                Title = Colors.White, Artist = Color.FromRgb(0xAA, 0xAA, 0xAA), Accent = Color.FromRgb(0x8A, 0xB4, 0xF8),
                Lyric = Colors.White, Glow = Colors.Transparent, GlowBlur = 0,
                LyricBoxBg = Color.FromRgb(0x26, 0x26, 0x26), LyricBoxBorder = Color.FromRgb(0x3A, 0x3A, 0x3A),
                MiniBg = Color.FromRgb(0x26, 0x26, 0x26), MiniBorder = Color.FromRgb(0x3A, 0x3A, 0x3A), MiniIcon = PixelArt.CreateNoteIcon,
            },

            PlayerSkin.Lofi => new SkinTheme
            {
                Font = new FontFamily("Georgia"),
                Title = Color.FromRgb(0xE8, 0xD9, 0xC0), Artist = Color.FromRgb(0xB8, 0x96, 0x6E), Accent = Color.FromRgb(0xC8, 0x96, 0x66),
                Lyric = Color.FromRgb(0xE8, 0xD9, 0xC0), Glow = Colors.Black, GlowBlur = 2,
                LyricBoxBg = Color.FromArgb(0xCC, 0x1A, 0x12, 0x0C), LyricBoxBorder = Color.FromRgb(0x8A, 0x6A, 0x4A),
                MiniBg = Color.FromRgb(0x4A, 0x2F, 0x1E), MiniBorder = Color.FromRgb(0x8A, 0x6A, 0x4A), MiniIcon = PixelArt.CreateCoffeeCup,
            },

            PlayerSkin.Aurora => new SkinTheme
            {
                Font = new FontFamily("Segoe UI Light"),
                Title = Color.FromRgb(0xE4, 0xF9, 0xF5), Artist = Color.FromRgb(0x9F, 0xD8, 0xCE), Accent = Color.FromRgb(0x4F, 0xD8, 0xC4),
                Lyric = Color.FromRgb(0xBF, 0xEF, 0xFF), Glow = Color.FromRgb(0x4F, 0xD8, 0xC4), GlowBlur = 5,
                LyricBoxBg = Color.FromArgb(0xB3, 0x0B, 0x1E, 0x2D), LyricBoxBorder = Color.FromRgb(0x4F, 0xD8, 0xC4),
                MiniBg = Color.FromRgb(0x0B, 0x1E, 0x2D), MiniBorder = Color.FromRgb(0x4F, 0xD8, 0xC4), MiniIcon = PixelArt.CreateMoonIcon,
            },

            PlayerSkin.Rain => new SkinTheme
            {
                Font = new FontFamily("Segoe UI Light"),
                Title = Color.FromRgb(0xC9, 0xDA, 0xE3), Artist = Color.FromRgb(0x8A, 0xA3, 0xB0), Accent = Color.FromRgb(0xA8, 0xC5, 0xD6),
                Lyric = Color.FromRgb(0xA8, 0xC5, 0xD6), Glow = Color.FromRgb(0x4A, 0x65, 0x72), GlowBlur = 3,
                LyricBoxBg = Color.FromArgb(0xCC, 0x0D, 0x13, 0x18), LyricBoxBorder = Color.FromRgb(0x4A, 0x65, 0x72),
                MiniBg = Color.FromRgb(0x0D, 0x13, 0x18), MiniBorder = Color.FromRgb(0x4A, 0x65, 0x72), MiniIcon = PixelArt.CreateRaindropIcon,
            },

            PlayerSkin.Starry => new SkinTheme
            {
                Font = new FontFamily("Segoe UI Light"),
                Title = Colors.White, Artist = Color.FromRgb(0xA8, 0xB4, 0xE8), Accent = Color.FromRgb(0x6B, 0x7F, 0xD7),
                Lyric = Color.FromRgb(0xC9, 0xD6, 0xFF), Glow = Color.FromRgb(0x6B, 0x7F, 0xD7), GlowBlur = 4,
                LyricBoxBg = Color.FromArgb(0xCC, 0x0A, 0x0E, 0x27), LyricBoxBorder = Color.FromRgb(0x6B, 0x7F, 0xD7),
                MiniBg = Color.FromRgb(0x0A, 0x0E, 0x27), MiniBorder = Color.FromRgb(0x6B, 0x7F, 0xD7), MiniIcon = PixelArt.CreateSparkleIcon,
            },

            PlayerSkin.Campfire => new SkinTheme
            {
                Font = new FontFamily("Georgia"),
                Title = Color.FromRgb(0xF5, 0xE0, 0xC8), Artist = Color.FromRgb(0xD9, 0x7B, 0x36), Accent = Color.FromRgb(0xF0, 0xA8, 0x68),
                Lyric = Color.FromRgb(0xF5, 0xE0, 0xC8), Glow = Color.FromRgb(0xD9, 0x7B, 0x36), GlowBlur = 3,
                LyricBoxBg = Color.FromArgb(0xCC, 0x12, 0x18, 0x0F), LyricBoxBorder = Color.FromRgb(0xD9, 0x7B, 0x36),
                MiniBg = Color.FromRgb(0x2A, 0x1A, 0x10), MiniBorder = Color.FromRgb(0xD9, 0x7B, 0x36), MiniIcon = PixelArt.CreateCampfire,
            },

            PlayerSkin.Sakura => new SkinTheme
            {
                Font = new FontFamily("Segoe UI Light"),
                Title = Color.FromRgb(0xFB, 0xE4, 0xEE), Artist = Color.FromRgb(0xE8, 0xB8, 0xCE), Accent = Color.FromRgb(0xF7, 0xA8, 0xC4),
                Lyric = Color.FromRgb(0xFB, 0xE4, 0xEE), Glow = Color.FromRgb(0xF7, 0xA8, 0xC4), GlowBlur = 3,
                LyricBoxBg = Color.FromArgb(0xCC, 0x24, 0x18, 0x26), LyricBoxBorder = Color.FromRgb(0xF7, 0xA8, 0xC4),
                MiniBg = Color.FromRgb(0x24, 0x18, 0x26), MiniBorder = Color.FromRgb(0xF7, 0xA8, 0xC4), MiniIcon = PixelArt.CreateBlossomIcon,
            },

            // 磁带机是唯一浅色背景的皮肤，标题/艺人文字要用深色才看得清；
            // 歌词框保留深棕底 + 米色字，做成"磁带标签窗口"的观感，跟其他深色皮肤的歌词框风格保持一致
            PlayerSkin.Cassette => new SkinTheme
            {
                Font = new FontFamily("Courier New"),
                Title = Color.FromRgb(0x3A, 0x2E, 0x22), Artist = Color.FromRgb(0x6B, 0x56, 0x42), Accent = Color.FromRgb(0xC1, 0x44, 0x0E),
                Lyric = Color.FromRgb(0xF0, 0xE6, 0xD2), Glow = Colors.Black, GlowBlur = 0,
                LyricBoxBg = Color.FromArgb(0xE6, 0x3A, 0x2E, 0x22), LyricBoxBorder = Color.FromRgb(0x8A, 0x6A, 0x4A),
                MiniBg = Color.FromRgb(0xE8, 0xD9, 0xC0), MiniBorder = Color.FromRgb(0x8A, 0x6A, 0x4A), MiniIcon = PixelArt.CreateCassetteIcon,
            },

            // 云朵漂浮风：淡蓝天空是唯一比磁带机更亮的浅色背景，标题/艺人也要用深色才看得清
            PlayerSkin.Cloud => new SkinTheme
            {
                Font = new FontFamily("Segoe UI Light"),
                Title = Color.FromRgb(0x1E, 0x3A, 0x5F), Artist = Color.FromRgb(0x3C, 0x6E, 0x91), Accent = Color.FromRgb(0x5F, 0xA8, 0xD3),
                Lyric = Color.FromRgb(0xF0, 0xF8, 0xFF), Glow = Color.FromRgb(0x5F, 0xA8, 0xD3), GlowBlur = 3,
                LyricBoxBg = Color.FromArgb(0xCC, 0x1B, 0x3A, 0x55), LyricBoxBorder = Color.FromRgb(0x5F, 0xA8, 0xD3),
                MiniBg = Color.FromRgb(0x7F, 0xB8, 0xDE), MiniBorder = Colors.White, MiniIcon = PixelArt.CreateCloudIcon,
            },

            PlayerSkin.Candle => new SkinTheme
            {
                Font = new FontFamily("Georgia"),
                Title = Color.FromRgb(0xF0, 0xDE, 0xB4), Artist = Color.FromRgb(0xB8, 0x98, 0x60), Accent = Color.FromRgb(0xD9, 0xA6, 0x48),
                Lyric = Color.FromRgb(0xF5, 0xE6, 0xC8), Glow = Color.FromRgb(0xD9, 0xA6, 0x48), GlowBlur = 4,
                LyricBoxBg = Color.FromArgb(0xCC, 0x1A, 0x14, 0x0E), LyricBoxBorder = Color.FromRgb(0x8A, 0x6A, 0x3A),
                MiniBg = Color.FromRgb(0x2A, 0x20, 0x18), MiniBorder = Color.FromRgb(0xD9, 0xA6, 0x48), MiniIcon = PixelArt.CreateCandle,
            },

            PlayerSkin.Plant => new SkinTheme
            {
                Font = new FontFamily("Segoe UI"),
                Title = Color.FromRgb(0xE8, 0xDC, 0xC8), Artist = Color.FromRgb(0xA8, 0x96, 0x7A), Accent = Color.FromRgb(0x8F, 0xBC, 0x7A),
                Lyric = Color.FromRgb(0xE8, 0xDC, 0xC8), Glow = Color.FromRgb(0x7F, 0xA6, 0x6B), GlowBlur = 2,
                LyricBoxBg = Color.FromArgb(0xCC, 0x1A, 0x14, 0x0E), LyricBoxBorder = Color.FromRgb(0x6A, 0x5A, 0x42),
                MiniBg = Color.FromRgb(0x2A, 0x1F, 0x16), MiniBorder = Color.FromRgb(0x7F, 0xA6, 0x6B), MiniIcon = PixelArt.CreatePlant,
            },

            PlayerSkin.Sunset => new SkinTheme
            {
                Font = new FontFamily("Segoe UI Light"),
                Title = Color.FromRgb(0xFF, 0xF3, 0xE0), Artist = Color.FromRgb(0xF2, 0xC6, 0xA0), Accent = Color.FromRgb(0xF9, 0xC7, 0x84),
                Lyric = Color.FromRgb(0xFF, 0xF3, 0xE0), Glow = Color.FromRgb(0xF9, 0xC7, 0x84), GlowBlur = 4,
                LyricBoxBg = Color.FromArgb(0xB3, 0x2A, 0x1F, 0x40), LyricBoxBorder = Color.FromRgb(0xF9, 0xC7, 0x84),
                MiniBg = Color.FromRgb(0xEA, 0x70, 0x93), MiniBorder = Color.FromRgb(0xF9, 0xC7, 0x84), MiniIcon = PixelArt.CreateSunsetIcon,
            },

            // 金黄色招牌/按钮配深紫黑柜体，跟赛博朋克那套的青/品红故意区分开，
            // 不然两套都是"暗色背景+霓虹发光"，放一起会显得像同一套皮肤的变体
            PlayerSkin.Arcade => new SkinTheme
            {
                Font = new FontFamily("Consolas"),
                Title = Colors.White, Artist = Color.FromRgb(0xC9, 0xA8, 0xE8), Accent = Color.FromRgb(0xFF, 0xC9, 0x3A),
                Lyric = Color.FromRgb(0xFF, 0xC9, 0x3A), Glow = Color.FromRgb(0xFF, 0xC9, 0x3A), GlowBlur = 8,
                LyricBoxBg = Color.FromArgb(0xCC, 0x14, 0x0C, 0x1C), LyricBoxBorder = Color.FromRgb(0xFF, 0xC9, 0x3A),
                MiniBg = Color.FromRgb(0x24, 0x18, 0x32), MiniBorder = Color.FromRgb(0xFF, 0xC9, 0x3A), MiniIcon = PixelArt.CreateJoystickIcon,
            },

            // 荧光青柠绿配深蓝黑背景，跟 CRT 那套的纯绿、极光雪夜的冷蓝故意错开一点色相
            PlayerSkin.Invaders => new SkinTheme
            {
                Font = new FontFamily("Consolas"),
                Title = Colors.White, Artist = Color.FromRgb(0x8A, 0xB8, 0x9E), Accent = Color.FromRgb(0xAE, 0xFF, 0x3A),
                Lyric = Color.FromRgb(0xAE, 0xFF, 0x3A), Glow = Color.FromRgb(0xAE, 0xFF, 0x3A), GlowBlur = 6,
                LyricBoxBg = Color.FromArgb(0xCC, 0x0A, 0x05, 0x18), LyricBoxBorder = Color.FromRgb(0xAE, 0xFF, 0x3A),
                MiniBg = Color.FromRgb(0x0A, 0x05, 0x18), MiniBorder = Color.FromRgb(0xAE, 0xFF, 0x3A), MiniIcon = PixelArt.CreateInvader,
            },

            // 暖黄车灯配冷紫夜色，都市夜景的"暖光源 + 冷背景"是这套皮肤的核心配色逻辑
            PlayerSkin.City => new SkinTheme
            {
                Font = new FontFamily("Segoe UI"),
                Title = Colors.White, Artist = Color.FromRgb(0xB8, 0xA8, 0xC8), Accent = Color.FromRgb(0xFF, 0xA9, 0x4A),
                Lyric = Color.FromRgb(0xFF, 0xE0, 0xC0), Glow = Color.FromRgb(0xFF, 0xA9, 0x4A), GlowBlur = 5,
                LyricBoxBg = Color.FromArgb(0xCC, 0x16, 0x10, 0x1F), LyricBoxBorder = Color.FromRgb(0xFF, 0xA9, 0x4A),
                MiniBg = Color.FromRgb(0x16, 0x10, 0x1F), MiniBorder = Color.FromRgb(0xFF, 0xA9, 0x4A), MiniIcon = PixelArt.CreateTrain,
            },

            // 第一套限定皮肤：深紫黑渐变 + 暖金强调色，故意跟赛博朋克的紫（冷调、配青色）、
            // 复古街机的金（配深紫黑柜体、但金色更亮更"街机霓虹"）都区分开，走的是"暗夜里一点
            // 低调的贵气"而不是霓虹灯那种张扬感——呼应它"很难拿到"的定位
            PlayerSkin.Crown => new SkinTheme
            {
                Font = new FontFamily("Georgia"),
                Title = Color.FromRgb(0xFF, 0xF3, 0xD6), Artist = Color.FromRgb(0xC9, 0xA8, 0x68), Accent = Color.FromRgb(0xE6, 0xB6, 0x55),
                Lyric = Color.FromRgb(0xFF, 0xF0, 0xD0), Glow = Color.FromRgb(0xE6, 0xB6, 0x55), GlowBlur = 8,
                LyricBoxBg = Color.FromArgb(0xCC, 0x16, 0x0B, 0x24), LyricBoxBorder = Color.FromRgb(0xE6, 0xB6, 0x55),
                MiniBg = Color.FromRgb(0x1A, 0x0B, 0x2E), MiniBorder = Color.FromRgb(0xE6, 0xB6, 0x55), MiniIcon = PixelArt.CreateCrown,
            },

            _ => new SkinTheme // Minecraft（默认）
            {
                Font = new FontFamily("Lucida Console"),
                Title = Colors.White, Artist = Color.FromRgb(0xDD, 0xDD, 0xDD), Accent = Color.FromRgb(0x55, 0xFF, 0x55),
                Lyric = Color.FromRgb(0xFF, 0xFF, 0x55), Glow = Color.FromRgb(0x3F, 0x3F, 0x00), GlowBlur = 0,
                LyricBoxBg = Color.FromArgb(0xD9, 0x11, 0x11, 0x11), LyricBoxBorder = Color.FromRgb(0x22, 0x22, 0x22),
                MiniBg = Color.FromRgb(0x3C, 0x8C, 0x3C), MiniBorder = Color.FromRgb(0x55, 0xFF, 0x55), MiniIcon = PixelArt.CreateTree,
            },
        };
    }
}
