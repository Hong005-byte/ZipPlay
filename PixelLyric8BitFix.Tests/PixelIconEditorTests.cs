using System.Collections.Generic;
using System.Windows.Media;
using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    public class PixelIconEditorTests
    {
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
            var original = Color.FromRgb(0xF9, 0xC7, 0x84);
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

            var palette = new Dictionary<char, Color>
            {
                ['#'] = Color.FromRgb(0xFF, 0x00, 0x00),
                ['w'] = Color.FromRgb(0x00, 0xFF, 0x00),
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

            var icon = PixelIconEditor.BuildIcon(grid, 4, 4, new Dictionary<char, Color>());

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

            var icon = PixelIconEditor.BuildIcon(grid, 4, 4, new Dictionary<char, Color> { ['#'] = Colors.Red });
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
            var palette = new Dictionary<char, Color> { ['#'] = Colors.Blue };

            var icon = PixelIconEditor.BuildIcon(grid, 4, 4, palette);
            var loaded = PixelIconEditor.TryLoadIcon(icon);

            Assert.NotNull(loaded);
            Assert.Equal(4, loaded!.Value.Width);
            Assert.Equal(4, loaded.Value.Height);
            Assert.Equal(grid[0, 0], loaded.Value.Grid[0, 0]);
            Assert.Equal(grid[1, 1], loaded.Value.Grid[1, 1]);
            Assert.Equal(Colors.Blue, loaded.Value.Palette['#']);
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
    }
}
