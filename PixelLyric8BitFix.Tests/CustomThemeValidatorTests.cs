using System;
using System.Linq;
using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    /// <summary>icon.rows 的尺寸校验——行数和每行宽度各自都要落在 MinIconSize~MaxIconSize 之间，
    /// 不要求正方形（内置皮肤里 Steve 就是 8x16 的长条形，同一套渲染逻辑本来就不挑尺寸）。</summary>
    public class CustomThemeValidatorTests
    {
        private static string BuildJson(int rows, int cols, string extraIconFields = "")
        {
            string rowLine = new string('#', cols);
            string rowsJson = string.Join(",\n", Enumerable.Repeat($"\"{rowLine}\"", rows));
            return $@"{{
              ""name"": ""test"",
              ""colors"": {{ ""title"": ""#FFFFFF"", ""artist"": ""#FFFFFF"", ""accent"": ""#FFFFFF"", ""lyric"": ""#FFFFFF"", ""lyricBoxBg"": ""#000000"", ""lyricBoxBorder"": ""#000000"" }},
              ""background"": {{ ""type"": ""solid"", ""stops"": [""#000000""] }},
              ""icon"": {{ ""palette"": {{ ""#"": ""#FFFFFF"" }}, ""rows"": [{rowsJson}] {extraIconFields} }},
              ""animation"": {{ ""type"": ""pulse"" }}
            }}";
        }

        [Theory]
        [InlineData(8, 8)]   // 最省事的默认尺寸，得继续能用，不能因为放宽范围就把老主题/懒人挤出去
        [InlineData(64, 64)] // 新放开的上限
        [InlineData(4, 4)]   // 下限
        [InlineData(8, 64)]  // 不强制正方形，长条形也要能过
        public void ParseAndValidate_SizeWithinRange_NoIconErrors(int rows, int cols)
        {
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(rows, cols));
            Assert.NotNull(theme);
            Assert.DoesNotContain(errors, e => e.Contains("icon.rows"));
        }

        [Theory]
        [InlineData(3, 8)]   // 行数比下限少 1
        [InlineData(65, 8)]  // 行数比上限多 1
        [InlineData(8, 3)]   // 宽度比下限少 1
        [InlineData(8, 65)]  // 宽度比上限多 1
        public void ParseAndValidate_SizeOutOfRange_ReportsIconError(int rows, int cols)
        {
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(rows, cols));
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("icon.rows"));
        }

        [Fact]
        public void ParseAndValidate_MismatchedRowWidths_ReportsError()
        {
            // 前 3 行宽 8、最后一行宽 7——行数落在合法范围内，纯粹是"每行必须一样宽"这条规则触发
            string json = $@"{{
              ""name"": ""test"",
              ""colors"": {{ ""title"": ""#FFFFFF"", ""artist"": ""#FFFFFF"", ""accent"": ""#FFFFFF"", ""lyric"": ""#FFFFFF"", ""lyricBoxBg"": ""#000000"", ""lyricBoxBorder"": ""#000000"" }},
              ""background"": {{ ""type"": ""solid"", ""stops"": [""#000000""] }},
              ""icon"": {{ ""palette"": {{ ""#"": ""#FFFFFF"" }}, ""rows"": [""########"", ""########"", ""########"", ""#######""] }},
              ""animation"": {{ ""type"": ""pulse"" }}
            }}";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(json);
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("不一致"));
        }

        private static string BuildJsonWithSensitivity(string? sensitivityField) => $@"{{
          ""name"": ""test"",
          ""colors"": {{ ""title"": ""#FFFFFF"", ""artist"": ""#FFFFFF"", ""accent"": ""#FFFFFF"", ""lyric"": ""#FFFFFF"", ""lyricBoxBg"": ""#000000"", ""lyricBoxBorder"": ""#000000"" }},
          ""background"": {{ ""type"": ""solid"", ""stops"": [""#000000""] }},
          ""icon"": {{ ""palette"": {{ ""#"": ""#FFFFFF"" }}, ""rows"": [""####"", ""####"", ""####"", ""####""] }},
          ""animation"": {{ ""type"": ""pulse""{sensitivityField} }}
        }}";

        [Fact]
        public void ParseAndValidate_SensitivityOmitted_NoError()
        {
            // 不填 sensitivity 得继续能用（等同 medium），不能因为加了这个可选字段就让老主题的 JSON 突然过不了校验
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJsonWithSensitivity(""));
            Assert.NotNull(theme);
            Assert.Empty(errors);
        }

        [Theory]
        [InlineData("low")]
        [InlineData("MEDIUM")] // 大小写不敏感
        [InlineData("high")]
        public void ParseAndValidate_ValidSensitivity_NoError(string sensitivity)
        {
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJsonWithSensitivity($@", ""sensitivity"": ""{sensitivity}"""));
            Assert.NotNull(theme);
            Assert.Empty(errors);
        }

        [Fact]
        public void ParseAndValidate_InvalidSensitivity_ReportsError()
        {
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJsonWithSensitivity(@", ""sensitivity"": ""extreme"""));
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("animation.sensitivity"));
        }

        private static string BuildJsonWithLayersField(string layersField) => $@"{{
          ""name"": ""test"",
          ""colors"": {{ ""title"": ""#FFFFFF"", ""artist"": ""#FFFFFF"", ""accent"": ""#FFFFFF"", ""lyric"": ""#FFFFFF"", ""lyricBoxBg"": ""#000000"", ""lyricBoxBorder"": ""#000000"" }},
          ""background"": {{ ""type"": ""solid"", ""stops"": [""#000000""] }},
          ""icon"": {{ ""palette"": {{ ""#"": ""#FFFFFF"" }}, ""rows"": [""####"", ""####"", ""####"", ""####""] }},
          ""animation"": {{ ""type"": ""pulse"" }}{layersField}
        }}";

        private const string OneValidLayer = @",
          ""layers"": [
            {
              ""anchor"": ""top-left"",
              ""icon"": { ""palette"": { ""#"": ""#FFFFFF"" }, ""rows"": [""####"", ""####"", ""####"", ""####""] },
              ""animation"": { ""type"": ""twinkle"" }
            }
          ]";

        [Fact]
        public void ParseAndValidate_LayersOmitted_NoError()
        {
            // 不填 layers 得继续能用，这是它加进来之前唯一的行为，老主题不该受影响
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJsonWithLayersField(""));
            Assert.NotNull(theme);
            Assert.Empty(errors);
        }

        [Fact]
        public void ParseAndValidate_OneValidLayer_NoError()
        {
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJsonWithLayersField(OneValidLayer));
            Assert.NotNull(theme);
            Assert.Empty(errors);
            Assert.Single(theme!.Layers!);
        }

        [Fact]
        public void ParseAndValidate_TwoValidLayers_NoError()
        {
            string twoLayers = @",
          ""layers"": [
            { ""anchor"": ""top-left"", ""icon"": { ""palette"": { ""#"": ""#FFFFFF"" }, ""rows"": [""####"", ""####"", ""####"", ""####""] }, ""animation"": { ""type"": ""twinkle"" } },
            { ""anchor"": ""bottom-right"", ""icon"": { ""palette"": { ""#"": ""#FFFFFF"" }, ""rows"": [""####"", ""####"", ""####"", ""####""] }, ""animation"": { ""type"": ""bob"" } }
          ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJsonWithLayersField(twoLayers));
            Assert.NotNull(theme);
            Assert.Empty(errors);
            Assert.Equal(2, theme!.Layers!.Count);
        }

        [Fact]
        public void ParseAndValidate_ThreeLayers_ExceedsMax_ReportsError()
        {
            string threeLayers = @",
          ""layers"": [
            { ""anchor"": ""top-left"", ""icon"": { ""palette"": { ""#"": ""#FFFFFF"" }, ""rows"": [""####"", ""####"", ""####"", ""####""] }, ""animation"": { ""type"": ""twinkle"" } },
            { ""anchor"": ""top-right"", ""icon"": { ""palette"": { ""#"": ""#FFFFFF"" }, ""rows"": [""####"", ""####"", ""####"", ""####""] }, ""animation"": { ""type"": ""bob"" } },
            { ""anchor"": ""bottom-left"", ""icon"": { ""palette"": { ""#"": ""#FFFFFF"" }, ""rows"": [""####"", ""####"", ""####"", ""####""] }, ""animation"": { ""type"": ""sway"" } }
          ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJsonWithLayersField(threeLayers));
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("\"layers\"") && e.Contains("最多"));
        }

        [Fact]
        public void ParseAndValidate_LayerMissingAnchor_ReportsError()
        {
            string layer = @",
          ""layers"": [
            { ""icon"": { ""palette"": { ""#"": ""#FFFFFF"" }, ""rows"": [""####"", ""####"", ""####"", ""####""] }, ""animation"": { ""type"": ""twinkle"" } }
          ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJsonWithLayersField(layer));
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("layers[0].anchor"));
        }

        [Fact]
        public void ParseAndValidate_LayerInvalidAnchor_ReportsError()
        {
            string layer = @",
          ""layers"": [
            { ""anchor"": ""middle"", ""icon"": { ""palette"": { ""#"": ""#FFFFFF"" }, ""rows"": [""####"", ""####"", ""####"", ""####""] }, ""animation"": { ""type"": ""twinkle"" } }
          ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJsonWithLayersField(layer));
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("layers[0].anchor"));
        }

        [Fact]
        public void ParseAndValidate_LayerBadIconRows_ReportsPrefixedError()
        {
            // 第 2 层（索引 1）图标行宽不一致——错误信息要能定位到具体是哪一层，不是笼统的 "icon.rows"
            string layer = @",
          ""layers"": [
            { ""anchor"": ""top-left"", ""icon"": { ""palette"": { ""#"": ""#FFFFFF"" }, ""rows"": [""####"", ""####"", ""####"", ""####""] }, ""animation"": { ""type"": ""twinkle"" } },
            { ""anchor"": ""bottom-right"", ""icon"": { ""palette"": { ""#"": ""#FFFFFF"" }, ""rows"": [""####"", ""###"", ""####"", ""####""] }, ""animation"": { ""type"": ""bob"" } }
          ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJsonWithLayersField(layer));
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("layers[1].icon.rows"));
        }

        [Fact]
        public void ParseAndValidate_LayerInvalidAnimationType_ReportsPrefixedError()
        {
            string layer = @",
          ""layers"": [
            { ""anchor"": ""top-left"", ""icon"": { ""palette"": { ""#"": ""#FFFFFF"" }, ""rows"": [""####"", ""####"", ""####"", ""####""] }, ""animation"": { ""type"": ""explode"" } }
          ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJsonWithLayersField(layer));
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("layers[0].animation.type"));
        }
    }
}
