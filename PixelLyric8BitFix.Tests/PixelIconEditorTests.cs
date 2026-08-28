using System.Collections.Generic;
using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    public class PixelIconEditorTests
    {
        // PixelIconEditor 现在住在 PixelLyric8Bit.Core，颜色用平台无关的 RgbaColor（不是
        // System.Windows.Media.Color）——这几个是测试里图方便的几个常用色，对应关系跟 WPF 的
        // Colors.Red/Blue/Lime/Yellow 一样（WPF 的 Colors.Green 其实是暗绿色，这里不需要用到，
        // 用到的是纯绿 Colors.Lime）。
        private static readonly RgbaColor TestRed = new(255, 0xFF, 0x00, 0x00);
        private static readonly RgbaColor TestBlue = new(255, 0x00, 0x00, 0xFF);
        private static readonly RgbaColor TestLime = new(255, 0x00, 0xFF, 0x00);
        private static readonly RgbaColor TestYellow = new(255, 0xFF, 0xFF, 0x00);

        [Fact]
        public void NextAvailableChar_SkipsAlreadyUsedChars_ReturnsFirstFreeOne()
        {
            char? next = PixelIconEditor.NextAvailableChar(new[] { '#', 'o', 'w' });
            Assert.Equal('r', next); // AssignableChars 里 '#','o','w' 之后紧跟着的是 'r'
        }

        [Fact]
        public void NextAvailableChar_AllCharsUsed_ReturnsNull()
        {
            char? next = PixelIconEditor.NextAvailableChar(PixelIconEditor.AssignableChars);
            Assert.Null(next);
        }

        [Fact]
        public void ColorToHex_RoundTripsWithTryParseHex()
        {
            var original = new RgbaColor(255, 0xF9, 0xC7, 0x84);
            string hex = PixelIconEditor.ColorToHex(original);
            Assert.Equal("#F9C784", hex);

            Assert.True(PixelIconEditor.TryParseHex(hex, out var parsed));
            Assert.Equal(original, parsed);
        }

        [Fact]
        public void BuildIcon_OnlyIncludesColorsActuallyUsedInGrid()
        {
            // 4x4 网格，只用了 '#' 这一个字符，调色板里额外配了个 'w'（对应画板里选过、但没画到格子上的颜色）——
            // 输出的 palette 不该带上 'w'，只带真的用到的 '#'
            var grid = new char[4, 4];
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    grid[y, x] = (x == 0 && y == 0) ? '#' : '.';

            var palette = new Dictionary<char, RgbaColor>
            {
                ['#'] = new RgbaColor(255, 0xFF, 0x00, 0x00),
                ['w'] = new RgbaColor(255, 0x00, 0xFF, 0x00),
            };

            var icon = PixelIconEditor.BuildIcon(grid, 4, 4, palette);

            Assert.Single(icon.Palette!);
            Assert.Equal("#FF0000", icon.Palette!["#"]);
            Assert.Equal(4, icon.Rows!.Count);
            Assert.Equal("#...", icon.Rows[0]);
            Assert.Equal("....", icon.Rows[1]);
        }

        [Fact]
        public void BuildIcon_EmptyGrid_ProducesEmptyPaletteAndAllDotRows()
        {
            var grid = new char[4, 4];
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    grid[y, x] = '.';

            var icon = PixelIconEditor.BuildIcon(grid, 4, 4, new Dictionary<char, RgbaColor>());

            Assert.Empty(icon.Palette!);
            Assert.All(icon.Rows!, row => Assert.Equal("....", row));
        }

        [Fact]
        public void BuildIcon_ThenParseAndValidate_PassesValidation()
        {
            // 端到端：画板输出的 icon 塞进一份完整主题 JSON，得能通过真正的校验器
            var grid = new char[4, 4];
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    grid[y, x] = '.';
            grid[0, 0] = '#'; grid[0, 1] = '#'; grid[1, 0] = '#'; grid[1, 1] = '#';

            var icon = PixelIconEditor.BuildIcon(grid, 4, 4, new Dictionary<char, RgbaColor> { ['#'] = TestRed });
            string iconJson = PixelIconEditor.SerializeIconFragment(icon);

            string themeJson = $@"{{
              ""name"": ""test"",
              ""colors"": {{ ""title"": ""#FFFFFF"", ""artist"": ""#FFFFFF"", ""accent"": ""#FFFFFF"", ""lyric"": ""#FFFFFF"", ""lyricBoxBg"": ""#000000"", ""lyricBoxBorder"": ""#000000"" }},
              ""background"": {{ ""type"": ""solid"", ""stops"": [""#000000""] }},
              ""icon"": {iconJson},
              ""animation"": {{ ""type"": ""pulse"" }}
            }}";

            var (theme, errors) = CustomThemeValidator.ParseAndValidate(themeJson);
            Assert.True(errors.Count == 0, string.Join(" | ", errors));
            Assert.NotNull(theme);
        }

        [Fact]
        public void TryLoadIcon_RoundTripsWithBuildIcon()
        {
            var grid = new char[4, 4];
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    grid[y, x] = (x + y) % 2 == 0 ? '#' : '.';
            var palette = new Dictionary<char, RgbaColor> { ['#'] = TestBlue };

            var icon = PixelIconEditor.BuildIcon(grid, 4, 4, palette);
            var loaded = PixelIconEditor.TryLoadIcon(icon);

            Assert.NotNull(loaded);
            Assert.Equal(4, loaded!.Value.Width);
            Assert.Equal(4, loaded.Value.Height);
            Assert.Equal(grid[0, 0], loaded.Value.Grid[0, 0]);
            Assert.Equal(grid[1, 1], loaded.Value.Grid[1, 1]);
            Assert.Equal(TestBlue, loaded.Value.Palette['#']);
        }

        [Fact]
        public void TryLoadIcon_MismatchedRowWidths_ReturnsNull()
        {
            var icon = new CustomThemeIcon
            {
                Rows = new List<string> { "####", "###" }, // 宽度不一致
                Palette = new Dictionary<string, string> { ["#"] = "#FF0000" },
            };
            Assert.Null(PixelIconEditor.TryLoadIcon(icon));
        }

        [Fact]
        public void TryLoadIcon_NoRows_ReturnsNull()
        {
            var icon = new CustomThemeIcon { Rows = new List<string>(), Palette = new Dictionary<string, string>() };
            Assert.Null(PixelIconEditor.TryLoadIcon(icon));
        }

        [Fact]
        public void TryExtractIcon_ValidThemeJson_ReturnsIcon()
        {
            string json = @"{ ""name"": ""x"", ""icon"": { ""palette"": { ""#"": ""#FF0000"" }, ""rows"": [""####"", ""####"", ""####"", ""####""] } }";
            var icon = PixelIconEditor.TryExtractIcon(json);
            Assert.NotNull(icon);
            Assert.Equal(4, icon!.Rows!.Count);
        }

        [Fact]
        public void TryExtractIcon_NoIconField_ReturnsNull()
        {
            Assert.Null(PixelIconEditor.TryExtractIcon(@"{ ""name"": ""x"" }"));
        }

        [Fact]
        public void TryExtractIcon_InvalidJson_ReturnsNullInsteadOfThrowing()
        {
            Assert.Null(PixelIconEditor.TryExtractIcon("{ not valid json"));
        }

        [Fact]
        public void TryExtractIcon_PascalCaseIconField_StillFound()
        {
            // 兼容旧格式存下来的文件（CustomThemeStore.Save 加 camelCase 设置之前存的）
            string json = @"{ ""Name"": ""x"", ""Icon"": { ""Palette"": { ""#"": ""#FF0000"" }, ""Rows"": [""####"", ""####"", ""####"", ""####""] } }";
            var icon = PixelIconEditor.TryExtractIcon(json);
            Assert.NotNull(icon);
        }

        [Fact]
        public void TryInsertIconIntoJson_ReplacesIconKeepsOtherFields()
        {
            string original = @"{ ""name"": ""保留我"", ""icon"": { ""palette"": {}, ""rows"": [""...."",""...."",""...."",""....""] } }";
            var newIcon = new CustomThemeIcon
            {
                Rows = new List<string> { "####", "####", "####", "####" },
                Palette = new Dictionary<string, string> { ["#"] = "#00FF00" },
            };

            string? result = PixelIconEditor.TryInsertIconIntoJson(original, newIcon);
            Assert.NotNull(result);
            Assert.Contains("保留我", result);
            Assert.Contains("#00FF00", result);
        }

        [Fact]
        public void TryInsertIconIntoJson_InvalidJson_ReturnsNull()
        {
            var icon = new CustomThemeIcon { Rows = new List<string> { "####" }, Palette = new Dictionary<string, string>() };
            Assert.Null(PixelIconEditor.TryInsertIconIntoJson("{ not valid", icon));
        }

        // ── 多帧（"帧"列表 UI）——BuildFrames / TryLoadFrames ──────────────────────────

        private static char[,] MakeGrid(int height, int width, char fill)
        {
            var grid = new char[height, width];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    grid[y, x] = fill;
            return grid;
        }

        [Fact]
        public void BuildFrames_SingleGrid_ProducesRowsNotFrames()
        {
            // 只有 1 帧的话不该包一层 frames——跟这个功能加进来之前的输出完全一样，
            // 不会因为用户压根没碰"帧"这个概念就平白多出一层结构
            var grid = MakeGrid(4, 4, '.');
            grid[0, 0] = '#';
            var icon = PixelIconEditor.BuildFrames(new[] { grid }, 4, 4, new Dictionary<char, RgbaColor> { ['#'] = TestRed });

            Assert.Null(icon.Frames);
            Assert.NotNull(icon.Rows);
            Assert.Equal("#...", icon.Rows![0]);
        }

        [Fact]
        public void BuildFrames_TwoGrids_ProducesFrames()
        {
            var frame1 = MakeGrid(4, 4, '.');
            frame1[0, 0] = '#';
            var frame2 = MakeGrid(4, 4, '.');
            frame2[0, 1] = '#';

            var icon = PixelIconEditor.BuildFrames(new[] { frame1, frame2 }, 4, 4, new Dictionary<char, RgbaColor> { ['#'] = TestRed });

            Assert.Null(icon.Rows);
            Assert.NotNull(icon.Frames);
            Assert.Equal(2, icon.Frames!.Count);
            Assert.Equal("#...", icon.Frames[0][0]);
            Assert.Equal(".#..", icon.Frames[1][0]);
        }

        [Fact]
        public void BuildFrames_CollectsUsedCharsAcrossAllFrames()
        {
            // 'w' 只出现在第 2 帧——最终 palette 里也该带上它，不是只看第 1 帧
            var frame1 = MakeGrid(4, 4, '.');
            frame1[0, 0] = '#';
            var frame2 = MakeGrid(4, 4, '.');
            frame2[0, 0] = 'w';

            var palette = new Dictionary<char, RgbaColor> { ['#'] = TestRed, ['w'] = TestBlue };
            var icon = PixelIconEditor.BuildFrames(new[] { frame1, frame2 }, 4, 4, palette);

            Assert.Equal(2, icon.Palette!.Count);
            Assert.Contains("w", icon.Palette.Keys);
        }

        [Fact]
        public void BuildFrames_ThenParseAndValidate_PassesValidation()
        {
            var frame1 = MakeGrid(4, 4, '.');
            frame1[0, 0] = '#';
            var frame2 = MakeGrid(4, 4, '.');
            frame2[0, 1] = '#';
            var icon = PixelIconEditor.BuildFrames(new[] { frame1, frame2 }, 4, 4, new Dictionary<char, RgbaColor> { ['#'] = TestRed });
            string iconJson = PixelIconEditor.SerializeIconFragment(icon);

            string themeJson = $@"{{
              ""name"": ""test"",
              ""colors"": {{ ""title"": ""#FFFFFF"", ""artist"": ""#FFFFFF"", ""accent"": ""#FFFFFF"", ""lyric"": ""#FFFFFF"", ""lyricBoxBg"": ""#000000"", ""lyricBoxBorder"": ""#000000"" }},
              ""background"": {{ ""type"": ""solid"", ""stops"": [""#000000""] }},
              ""icon"": {iconJson},
              ""animation"": {{ ""type"": ""pulse"" }}
            }}";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(themeJson);
            Assert.True(errors.Count == 0, string.Join(" | ", errors));
            Assert.NotNull(theme);
        }

        [Fact]
        public void TryLoadFrames_WithFrames_RoundTripsWithBuildFrames()
        {
            var frame1 = MakeGrid(4, 4, '.');
            frame1[0, 0] = '#';
            var frame2 = MakeGrid(4, 4, '.');
            frame2[1, 1] = '#';
            var icon = PixelIconEditor.BuildFrames(new[] { frame1, frame2 }, 4, 4, new Dictionary<char, RgbaColor> { ['#'] = TestBlue });

            var loaded = PixelIconEditor.TryLoadFrames(icon);

            Assert.NotNull(loaded);
            Assert.Equal(2, loaded!.Value.Grids.Count);
            Assert.Equal(4, loaded.Value.Width);
            Assert.Equal(4, loaded.Value.Height);
            Assert.Equal('#', loaded.Value.Grids[0][0, 0]);
            Assert.Equal('#', loaded.Value.Grids[1][1, 1]);
            Assert.Equal(TestBlue, loaded.Value.Palette['#']);
        }

        [Fact]
        public void TryLoadFrames_WithoutFrames_FallsBackToSingleRowsFrame()
        {
            var icon = new CustomThemeIcon
            {
                Rows = new List<string> { "####", "####", "####", "####" },
                Palette = new Dictionary<string, string> { ["#"] = "#FF0000" },
            };
            var loaded = PixelIconEditor.TryLoadFrames(icon);

            Assert.NotNull(loaded);
            Assert.Single(loaded!.Value.Grids);
        }

        [Fact]
        public void TryLoadFrames_FrameSizesDiffer_ReturnsNull()
        {
            var icon = new CustomThemeIcon
            {
                Frames = new List<List<string>>
                {
                    new() { "####", "####", "####", "####" },
                    new() { "#####", "#####", "#####", "#####", "#####" },
                },
                Palette = new Dictionary<string, string>(),
            };
            Assert.Null(PixelIconEditor.TryLoadFrames(icon));
        }

        [Fact]
        public void TryLoadFrames_EmptyFramesList_ReturnsNull()
        {
            var icon = new CustomThemeIcon { Frames = new List<List<string>>(), Palette = new Dictionary<string, string>() };
            Assert.Null(PixelIconEditor.TryLoadFrames(icon));
        }

        // ── 动作（icon.actions，画板"动作"选择器用）──BuildIconWithActions / LoadActions ──────

        [Fact]
        public void BuildIconWithActions_NoExtraActions_MatchesBuildFrames()
        {
            var grid = MakeGrid(4, 4, '.');
            grid[0, 0] = '#';
            var palette = new Dictionary<char, RgbaColor> { ['#'] = TestRed };

            var icon = PixelIconEditor.BuildIconWithActions(new[] { grid }, 4, 4, palette,
                new List<PixelIconEditor.LoadedIconAction>());

            Assert.Null(icon.Actions); // 没有额外动作就不该多出一个空数组字段
            Assert.NotNull(icon.Rows);
            Assert.Equal("#...", icon.Rows![0]);
        }

        [Fact]
        public void BuildIconWithActions_OneExtraAction_ProducesActionWithWrappedFrames()
        {
            var action0 = MakeGrid(4, 4, '.');
            action0[0, 0] = '#';
            var action1Frame = MakeGrid(4, 4, '.');
            action1Frame[0, 1] = '#';

            var icon = PixelIconEditor.BuildIconWithActions(new[] { action0 }, 4, 4,
                new Dictionary<char, RgbaColor> { ['#'] = TestRed },
                new List<PixelIconEditor.LoadedIconAction> { new() { Name = "挥手", Grids = new List<char[,]> { action1Frame }, FrameDurationOverride = 0.5 } });

            Assert.NotNull(icon.Actions);
            Assert.Single(icon.Actions!);
            Assert.Equal("挥手", icon.Actions![0].Name);
            Assert.Equal(0.5, icon.Actions[0].FrameDuration);
            // 哪怕只有 1 帧，action.Frames 也要包成 [rows]（不像顶层 icon 有 Rows 简写）
            Assert.Single(icon.Actions[0].Frames!);
            Assert.Equal(".#..", icon.Actions[0].Frames![0][0]);
        }

        [Fact]
        public void BuildIconWithActions_CollectsUsedCharsFromActionsToo()
        {
            // 'w' 只出现在额外动作里，不在动作 0——最终 palette 也该带上它
            var action0 = MakeGrid(4, 4, '.');
            action0[0, 0] = '#';
            var action1Frame = MakeGrid(4, 4, '.');
            action1Frame[0, 0] = 'w';

            var palette = new Dictionary<char, RgbaColor> { ['#'] = TestRed, ['w'] = TestBlue };
            var icon = PixelIconEditor.BuildIconWithActions(new[] { action0 }, 4, 4, palette,
                new List<PixelIconEditor.LoadedIconAction> { new() { Grids = new List<char[,]> { action1Frame } } });

            Assert.Equal(2, icon.Palette!.Count);
            Assert.Contains("w", icon.Palette.Keys);
        }

        [Fact]
        public void BuildIconWithActions_ThenParseAndValidate_PassesValidation()
        {
            var action0 = MakeGrid(4, 4, '.');
            action0[0, 0] = '#';
            var action1Frame = MakeGrid(4, 4, '.');
            action1Frame[0, 1] = '#';

            var icon = PixelIconEditor.BuildIconWithActions(new[] { action0 }, 4, 4,
                new Dictionary<char, RgbaColor> { ['#'] = TestRed },
                new List<PixelIconEditor.LoadedIconAction> { new() { Name = "动作 1", Grids = new List<char[,]> { action1Frame } } });
            string iconJson = PixelIconEditor.SerializeIconFragment(icon);

            string themeJson = $@"{{
              ""name"": ""test"",
              ""colors"": {{ ""title"": ""#FFFFFF"", ""artist"": ""#FFFFFF"", ""accent"": ""#FFFFFF"", ""lyric"": ""#FFFFFF"", ""lyricBoxBg"": ""#000000"", ""lyricBoxBorder"": ""#000000"" }},
              ""background"": {{ ""type"": ""solid"", ""stops"": [""#000000""] }},
              ""icon"": {iconJson},
              ""animation"": {{ ""type"": ""pulse"" }}
            }}";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(themeJson);
            Assert.True(errors.Count == 0, string.Join(" | ", errors));
            Assert.NotNull(theme);
        }

        [Fact]
        public void LoadActions_RoundTripsWithBuildIconWithActions()
        {
            var action0 = MakeGrid(4, 4, '.');
            action0[0, 0] = '#';
            var action1Frame = MakeGrid(4, 4, '.');
            action1Frame[0, 1] = '#';

            var icon = PixelIconEditor.BuildIconWithActions(new[] { action0 }, 4, 4,
                new Dictionary<char, RgbaColor> { ['#'] = TestRed },
                new List<PixelIconEditor.LoadedIconAction> { new() { Name = "挥手", Grids = new List<char[,]> { action1Frame }, FrameDurationOverride = 0.3 } });

            var loaded = PixelIconEditor.LoadActions(icon, 4, 4);

            Assert.Single(loaded);
            Assert.Equal("挥手", loaded[0].Name);
            Assert.Equal(0.3, loaded[0].FrameDurationOverride);
            Assert.Single(loaded[0].Grids);
            Assert.Equal('#', loaded[0].Grids[0][0, 1]);
        }

        [Fact]
        public void LoadActions_RoundTripsAutoSwitchAndAnimation()
        {
            // 画板没有 UI 编 Animation，但必须原样带着走，不然"续画一个已经手写了 animation 的动作"
            // 会在插入回编辑框的时候把这份 animation 悄悄冲掉——这是 IconPainterWindow.xaml.cs 里
            // PaintedAction.Animation 那段注释说的"比功能不支持更糟的丢数据"场景，这里在 Core 层验证
            // BuildIconWithActions/LoadActions 这一对方法本身确实做到了完整往返
            var action0 = MakeGrid(4, 4, '.');
            var action1Frame = MakeGrid(4, 4, '.');
            var animation = new CustomThemeAnimation { Type = "spin", Duration = 1.5 };

            var icon = PixelIconEditor.BuildIconWithActions(new[] { action0 }, 4, 4,
                new Dictionary<char, RgbaColor>(),
                new List<PixelIconEditor.LoadedIconAction> { new() { Grids = new List<char[,]> { action1Frame }, AutoSwitchAfterSeconds = 90, Animation = animation } });

            Assert.Equal(90, icon.Actions![0].AutoSwitchAfterSeconds);
            Assert.Same(animation, icon.Actions[0].Animation);

            var loaded = PixelIconEditor.LoadActions(icon, 4, 4);
            Assert.Equal(90, loaded[0].AutoSwitchAfterSeconds);
            Assert.Same(animation, loaded[0].Animation);
        }

        [Fact]
        public void LoadActions_NoActionsField_ReturnsEmptyList()
        {
            var icon = new CustomThemeIcon { Rows = new List<string> { "....", "....", "....", "...." }, Palette = new() };
            Assert.Empty(PixelIconEditor.LoadActions(icon, 4, 4));
        }

        [Fact]
        public void LoadActions_SizeMismatchWithAction0_SkipsThatActionButKeepsOthers()
        {
            // 第一个动作尺寸跟传入的 width/height（画板里 action 0 摊开出来的尺寸）对不上——
            // 画板要求跨动作同尺寸，这种续画不了，但不该连累第二个尺寸对的动作也没法续画
            var icon = new CustomThemeIcon
            {
                Rows = new List<string> { "....", "....", "....", "...." },
                Palette = new Dictionary<string, string> { ["#"] = "#FF0000" },
                Actions = new List<CustomThemeIconAction>
                {
                    new() { Frames = new List<List<string>> { new() { "#####", "#####", "#####", "#####", "#####" } } }, // 5x5，跟 4x4 对不上
                    new() { Name = "好的", Frames = new List<List<string>> { new() { "#...", "....", "....", "...." } } },
                },
            };

            var loaded = PixelIconEditor.LoadActions(icon, 4, 4);

            Assert.Single(loaded);
            Assert.Equal("好的", loaded[0].Name);
        }

        [Fact]
        public void LoadActions_ActionWithEmptyFrames_IsSkipped()
        {
            var icon = new CustomThemeIcon
            {
                Rows = new List<string> { "....", "....", "....", "...." },
                Palette = new(),
                Actions = new List<CustomThemeIconAction> { new() { Name = "空的", Frames = new List<List<string>>() } },
            };
            Assert.Empty(PixelIconEditor.LoadActions(icon, 4, 4));
        }

        // ── 桶装填充：FloodFill ────────────────────────────────────────────────

        [Fact]
        public void FloodFill_FillsConnectedRegionOnly_LeavesDisconnectedRegionUntouched()
        {
            // 4x4，左上 2x2 是 '#'，右下 2x2 是 'w'（两块不连通）——从左上角填成 'x'，
            // 右下那块 'w' 不该被动到
            var grid = new char[4, 4];
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    grid[y, x] = (x < 2 && y < 2) ? '#' : ((x >= 2 && y >= 2) ? 'w' : '.');

            bool changed = PixelIconEditor.FloodFill(grid, 4, 4, 0, 0, 'x');

            Assert.True(changed);
            Assert.Equal('x', grid[0, 0]);
            Assert.Equal('x', grid[1, 1]);
            Assert.Equal('w', grid[3, 3]); // 不连通的另一块颜色没被填到
            Assert.Equal('.', grid[0, 2]); // 中间的空白也没被误填
        }

        [Fact]
        public void FloodFill_TargetSameAsOrigin_ReturnsFalseAndDoesNotMutate()
        {
            var grid = new char[2, 2] { { '#', '#' }, { '#', '#' } };
            bool changed = PixelIconEditor.FloodFill(grid, 2, 2, 0, 0, '#');

            Assert.False(changed);
            Assert.Equal('#', grid[0, 0]);
        }

        [Fact]
        public void FloodFill_EntireGridSameColor_FillsWholeGrid()
        {
            var grid = new char[3, 3];
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                    grid[y, x] = '.';

            bool changed = PixelIconEditor.FloodFill(grid, 3, 3, 1, 1, '#');

            Assert.True(changed);
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                    Assert.Equal('#', grid[y, x]);
        }

        // ── 图片导入量化：QuantizeToGrid ──────────────────────────────────────

        [Fact]
        public void QuantizeToGrid_NullPixel_BecomesTransparentDot()
        {
            var pixels = new RgbaColor?[1, 2] { { TestRed, null } };
            var (grid, _) = PixelIconEditor.QuantizeToGrid(pixels, new Dictionary<char, RgbaColor>(), maxColors: 10);

            Assert.NotEqual('.', grid[0, 0]);
            Assert.Equal('.', grid[0, 1]);
        }

        [Fact]
        public void QuantizeToGrid_ColorMatchingExistingPalette_ReusesExistingCharInsteadOfAssigningNew()
        {
            var existing = new Dictionary<char, RgbaColor> { ['#'] = TestRed };
            var pixels = new RgbaColor?[1, 1] { { TestRed } };

            var (grid, newColors) = PixelIconEditor.QuantizeToGrid(pixels, existing, maxColors: 10);

            Assert.Equal('#', grid[0, 0]);
            Assert.Empty(newColors); // 复用已有颜色，不该占用新字符名额
        }

        [Fact]
        public void QuantizeToGrid_MoreDistinctColorsThanBudget_NeverExceedsMaxColors()
        {
            // 4 种截然不同的颜色，但只给 2 个名额——量化完新增的颜色种类不能超过预算
            var pixels = new RgbaColor?[1, 4]
            {
                { TestRed, TestLime, TestBlue, TestYellow },
            };

            var (grid, newColors) = PixelIconEditor.QuantizeToGrid(pixels, new Dictionary<char, RgbaColor>(), maxColors: 2);

            Assert.True(newColors.Count <= 2);
            // 网格里每一格都必须落在"新分配出来的颜色"这个集合里——没有孤立的、没被分配字符的符号
            for (int x = 0; x < 4; x++)
                Assert.Contains(grid[0, x], newColors.Keys);
        }

        [Fact]
        public void QuantizeToGrid_AllPixelsTransparent_ProducesAllDotsAndNoNewColors()
        {
            var pixels = new RgbaColor?[2, 2];
            var (grid, newColors) = PixelIconEditor.QuantizeToGrid(pixels, new Dictionary<char, RgbaColor>(), maxColors: 10);

            Assert.Empty(newColors);
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 2; x++)
                    Assert.Equal('.', grid[y, x]);
        }
    }
}
