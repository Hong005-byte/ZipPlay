namespace PixelLyric8BitFix
{
    /// <summary>
    /// Mobile 悬浮窗用的精简皮肤——桌面版一套皮肤是"配色 + 像素图标 + 专属动画"（见 SkinTheme.cs），
    /// 手机上是一小条悬浮窗，容不下那么多细节，先只搬"配色"这一层过来，用同一套色值（直接从
    /// PixelLyric8BitFix/SkinTheme.cs 里对应皮肤的 LyricBoxBg/Accent/Lyric 抄出来，不是自己配的新颜色，
    /// 保证跟桌面版看起来是"同一个皮肤"）；像素图标、专属动画、跟着音乐律动这些留到以后要不要做真正的
    /// 22 套全量移植时再说，见 README 里 Mobile 那节的路线图。
    ///
    /// Id 特意用跟桌面版 PlayerSkin 枚举成员完全一样的名字（Crt/Cyberpunk/Vinyl 这些），不是自己另起
    /// 一套——以后真要全量移植的时候，直接照着 PlayerSkin 剩下的成员往这个表里加就行，不用哪天回头
    /// 发现两边命名对不上又要重新映射一遍。
    /// </summary>
    public readonly record struct MobileSkinPalette(string Id, string DisplayName, RgbaColor Background, RgbaColor Accent, RgbaColor Text);

    public static class MobileSkinCatalog
    {
        public const string DefaultSkinId = "Simple";

        /// <summary>MobileSettingsStore.SelectedSkinId 存的是一个字符串——内置皮肤直接存 Id（"Simple"/
        /// "Crt"……），选了个客制化主题的话存这个前缀 + 文件名（"custom:3f2a…json"），FloatingOverlayService
        /// 读到这个前缀就知道该去 MobileCustomThemeStore 查而不是查这张内置表，见该文件里的 ApplySkin。</summary>
        public const string CustomThemePrefix = "custom:";

        public static readonly IReadOnlyList<MobileSkinPalette> All = new[]
        {
            new MobileSkinPalette("Simple", "简约风",
                Background: new RgbaColor(0xFF, 0x26, 0x26, 0x26),
                Accent: new RgbaColor(0xFF, 0x8A, 0xB4, 0xF8),
                Text: new RgbaColor(0xFF, 0xFF, 0xFF, 0xFF)),

            new MobileSkinPalette("Crt", "复古 CRT 终端风",
                Background: new RgbaColor(0xCC, 0x00, 0x00, 0x00),
                Accent: new RgbaColor(0xFF, 0x33, 0xFF, 0x66),
                Text: new RgbaColor(0xFF, 0x66, 0xFF, 0xAA)),

            new MobileSkinPalette("Cyberpunk", "霓虹赛博朋克风",
                Background: new RgbaColor(0xB3, 0x12, 0x00, 0x22),
                Accent: new RgbaColor(0xFF, 0x00, 0xF0, 0xFF),
                Text: new RgbaColor(0xFF, 0xFF, 0x2E, 0xD1)),

            new MobileSkinPalette("Vinyl", "黑胶唱片机风",
                Background: new RgbaColor(0xCC, 0x1A, 0x12, 0x0C),
                Accent: new RgbaColor(0xFF, 0xC9, 0xA2, 0x27),
                Text: new RgbaColor(0xFF, 0xF1, 0xE3, 0xC6)),

            new MobileSkinPalette("Aurora", "极光雪夜风",
                Background: new RgbaColor(0xB3, 0x0B, 0x1E, 0x2D),
                Accent: new RgbaColor(0xFF, 0x4F, 0xD8, 0xC4),
                Text: new RgbaColor(0xFF, 0xBF, 0xEF, 0xFF)),

            new MobileSkinPalette("Sakura", "樱花风",
                Background: new RgbaColor(0xCC, 0x24, 0x18, 0x26),
                Accent: new RgbaColor(0xFF, 0xF7, 0xA8, 0xC4),
                Text: new RgbaColor(0xFF, 0xFB, 0xE4, 0xEE)),

            // ↓↓↓ 这一批是阶段 6 补的——桌面版 22 套内置皮肤剩下的（除了 Custom，那是自定义主题的
            // 事，见 MobileCustomThemeStore；Crown 也在这张表里，但要不要能选它是 UI 层面的事，
            // 见 MainPage.xaml.cs 的 BuildSkinPicker 怎么用 AchievementCalculator.IsCrownSkinUnlocked
            // 过滤），同样是从 SkinTheme.cs 抄色值，不是新配的颜色
            new MobileSkinPalette("Glass", "玻璃拟态风",
                Background: new RgbaColor(0x33, 0xFF, 0xFF, 0xFF),
                Accent: new RgbaColor(0xFF, 0xFF, 0xFF, 0xFF),
                Text: new RgbaColor(0xFF, 0xFF, 0xFF, 0xFF)),

            new MobileSkinPalette("Lofi", "复古咖啡馆风",
                Background: new RgbaColor(0xCC, 0x1A, 0x12, 0x0C),
                Accent: new RgbaColor(0xFF, 0xC8, 0x96, 0x66),
                Text: new RgbaColor(0xFF, 0xE8, 0xD9, 0xC0)),

            new MobileSkinPalette("Rain", "雨夜窗景风",
                Background: new RgbaColor(0xCC, 0x0D, 0x13, 0x18),
                Accent: new RgbaColor(0xFF, 0xA8, 0xC5, 0xD6),
                Text: new RgbaColor(0xFF, 0xA8, 0xC5, 0xD6)),

            new MobileSkinPalette("Starry", "星空太空风",
                Background: new RgbaColor(0xCC, 0x0A, 0x0E, 0x27),
                Accent: new RgbaColor(0xFF, 0x6B, 0x7F, 0xD7),
                Text: new RgbaColor(0xFF, 0xC9, 0xD6, 0xFF)),

            new MobileSkinPalette("Campfire", "篝火露营风",
                Background: new RgbaColor(0xCC, 0x12, 0x18, 0x0F),
                Accent: new RgbaColor(0xFF, 0xF0, 0xA8, 0x68),
                Text: new RgbaColor(0xFF, 0xF5, 0xE0, 0xC8)),

            new MobileSkinPalette("Cassette", "复古磁带机风",
                Background: new RgbaColor(0xE6, 0x3A, 0x2E, 0x22),
                Accent: new RgbaColor(0xFF, 0xC1, 0x44, 0x0E),
                Text: new RgbaColor(0xFF, 0xF0, 0xE6, 0xD2)),

            new MobileSkinPalette("Cloud", "云朵漂浮风",
                Background: new RgbaColor(0xCC, 0x1B, 0x3A, 0x55),
                Accent: new RgbaColor(0xFF, 0x5F, 0xA8, 0xD3),
                Text: new RgbaColor(0xFF, 0xF0, 0xF8, 0xFF)),

            new MobileSkinPalette("Candle", "烛光冥想风",
                Background: new RgbaColor(0xCC, 0x1A, 0x14, 0x0E),
                Accent: new RgbaColor(0xFF, 0xD9, 0xA6, 0x48),
                Text: new RgbaColor(0xFF, 0xF5, 0xE6, 0xC8)),

            new MobileSkinPalette("Plant", "绿植角落风",
                Background: new RgbaColor(0xCC, 0x1A, 0x14, 0x0E),
                Accent: new RgbaColor(0xFF, 0x8F, 0xBC, 0x7A),
                Text: new RgbaColor(0xFF, 0xE8, 0xDC, 0xC8)),

            new MobileSkinPalette("Sunset", "海边黄昏风",
                Background: new RgbaColor(0xB3, 0x2A, 0x1F, 0x40),
                Accent: new RgbaColor(0xFF, 0xF9, 0xC7, 0x84),
                Text: new RgbaColor(0xFF, 0xFF, 0xF3, 0xE0)),

            new MobileSkinPalette("Arcade", "复古街机风",
                Background: new RgbaColor(0xCC, 0x14, 0x0C, 0x1C),
                Accent: new RgbaColor(0xFF, 0xFF, 0xC9, 0x3A),
                Text: new RgbaColor(0xFF, 0xFF, 0xC9, 0x3A)),

            new MobileSkinPalette("Invaders", "8-bit 太空侵略者风",
                Background: new RgbaColor(0xCC, 0x0A, 0x05, 0x18),
                Accent: new RgbaColor(0xFF, 0xAE, 0xFF, 0x3A),
                Text: new RgbaColor(0xFF, 0xAE, 0xFF, 0x3A)),

            new MobileSkinPalette("City", "都市夜景风",
                Background: new RgbaColor(0xCC, 0x16, 0x10, 0x1F),
                Accent: new RgbaColor(0xFF, 0xFF, 0xA9, 0x4A),
                Text: new RgbaColor(0xFF, 0xFF, 0xE0, 0xC0)),

            new MobileSkinPalette("Minecraft", "Minecraft 像素风",
                Background: new RgbaColor(0xD9, 0x11, 0x11, 0x11),
                Accent: new RgbaColor(0xFF, 0x55, 0xFF, 0x55),
                Text: new RgbaColor(0xFF, 0xFF, 0xFF, 0x55)),

            // 限定皮肤——要不要显示在选择器里由 UI 层判断解锁状态，见文件顶部注释；
            // 表里始终有这一条数据，方便任何地方（比如以后的分享卡片）直接查它的配色
            new MobileSkinPalette("Crown", "尊贵皇冠风·限定",
                Background: new RgbaColor(0xCC, 0x16, 0x0B, 0x24),
                Accent: new RgbaColor(0xFF, 0xE6, 0xB6, 0x55),
                Text: new RgbaColor(0xFF, 0xFF, 0xF0, 0xD0)),
        };

        public static MobileSkinPalette Find(string? id)
        {
            foreach (var palette in All)
            {
                if (palette.Id == id) return palette;
            }
            return All[0]; // 存的 id 是旧版本/脏数据对不上号，兜底回第一套皮肤，不能让悬浮窗直接崩掉
        }

        /// <summary>把一份客制化主题的颜色抽成一份精简皮肤——只搬 Colors 这一层（LyricBoxBg -> Background、
        /// Accent -> Accent、Lyric -> Text，跟内置皮肤表里同名字段抄的是同一套对应关系，见文件顶部注释），
        /// 像素图标/逐帧动画/多层装饰这些客制化主题独有的视觉细节，精简版悬浮窗还画不出来。
        /// theme.Colors 在存进 MobileCustomThemeStore 之前已经过 CustomThemeValidator.ParseAndValidate
        /// 校验，这里不重复校验，纯粹是格式转换——万一真拿到一份没校验过的脏数据，颜色解析失败就
        /// 落回透明，不会抛异常。</summary>
        public static MobileSkinPalette FromCustomTheme(CustomTheme theme)
        {
            CustomThemeValidator.TryParseHexColor(theme.Colors?.LyricBoxBg ?? "", out var background);
            CustomThemeValidator.TryParseHexColor(theme.Colors?.Accent ?? "", out var accent);
            CustomThemeValidator.TryParseHexColor(theme.Colors?.Lyric ?? "", out var text);
            return new MobileSkinPalette(
                Id: CustomThemePrefix, // 调用方（ApplySkin）自己知道完整的 "custom:文件名"，这里的 Id 不重要，不参与 Find 比对
                DisplayName: string.IsNullOrWhiteSpace(theme.Name) ? "客制化主题" : theme.Name!,
                Background: background,
                Accent: accent,
                Text: text);
        }
    }
}
