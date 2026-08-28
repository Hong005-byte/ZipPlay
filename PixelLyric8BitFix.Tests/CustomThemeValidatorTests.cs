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

        [Theory]
        [InlineData("pulse")]
        [InlineData("pulse+sway")]
        [InlineData("pulse+sway+twinkle+bob")] // 4 个互不占用同一属性的招式叠一起，理论上限
        [InlineData("PULSE+SWAY")] // 大小写不敏感
        public void ParseAndValidate_MainIconComboWithoutDriftFall_NoError(string type)
        {
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJsonWithSensitivity("").Replace(@"""type"": ""pulse""", $@"""type"": ""{type}"""));
            Assert.NotNull(theme);
            Assert.Empty(errors);
        }

        [Theory]
        [InlineData("drift+pulse")]
        [InlineData("pulse+fall")]
        [InlineData("drift+fall")]
        public void ParseAndValidate_MainIconComboWithDriftFall_ReportsError(string type)
        {
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJsonWithSensitivity("").Replace(@"""type"": ""pulse""", $@"""type"": ""{type}"""));
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("animation.type") && e.Contains("drift/fall"));
        }

        [Fact]
        public void ParseAndValidate_MainIconComboWithOneUnknownType_ReportsErrorForThatTokenOnly()
        {
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJsonWithSensitivity("").Replace(@"""type"": ""pulse""", @"""type"": ""pulse+explode"""));
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("\"explode\""));
            Assert.DoesNotContain(errors, e => e.Contains("\"pulse\"") && e.Contains("不认识"));
        }

        [Fact]
        public void ParseAndValidate_LayerAllowsDriftFallCombo_NoError()
        {
            // 层没有主图标那个三图标专属轨道的结构性限制，drift 跟别的招式组合应该直接放行
            string layer = @",
          ""layers"": [
            { ""anchor"": ""top-left"", ""icon"": { ""palette"": { ""#"": ""#FFFFFF"" }, ""rows"": [""####"", ""####"", ""####"", ""####""] }, ""animation"": { ""type"": ""drift+pulse"" } }
          ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJsonWithLayersField(layer));
            Assert.NotNull(theme);
            Assert.Empty(errors);
        }

        [Fact]
        public void ParseAndValidate_MainIconWalkSolo_NoError()
        {
            // walk（装饰栏里来回走，跟 Minecraft 皮肤 Steve 同一套手法）单独使用是合法的第 9 招
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJsonWithSensitivity("").Replace(@"""type"": ""pulse""", @"""type"": ""walk"""));
            Assert.NotNull(theme);
            Assert.Empty(errors);
        }

        [Theory]
        [InlineData("walk+pulse")]
        [InlineData("pulse+walk")]
        [InlineData("walk+drift")]
        [InlineData("walk+fall")]
        public void ParseAndValidate_MainIconComboWithWalk_ReportsError(string type)
        {
            // walk 跟 drift/fall 是同一类"需要专属渲染结构、只能单独出现"的招式——不能跟别的组合，
            // 也不能跟 drift/fall 互相组合
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJsonWithSensitivity("").Replace(@"""type"": ""pulse""", $@"""type"": ""{type}"""));
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("animation.type") && e.Contains("drift/fall/walk"));
        }

        [Fact]
        public void ParseAndValidate_LayerAllowsWalkCombo_NoError()
        {
            // 层没有 Steve 那样的独立装饰带，walk 跟别的招式组合应该直接放行（退化成跟 drift 一样的
            // 原地小幅摆动，见 MainWindow.Skins.cs 的 StartLayerAnimation）
            string layer = @",
          ""layers"": [
            { ""anchor"": ""top-left"", ""icon"": { ""palette"": { ""#"": ""#FFFFFF"" }, ""rows"": [""####"", ""####"", ""####"", ""####""] }, ""animation"": { ""type"": ""walk+pulse"" } }
          ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJsonWithLayersField(layer));
            Assert.NotNull(theme);
            Assert.Empty(errors);
        }

        [Theory]
        [InlineData("pulse", new[] { "pulse" })]
        [InlineData("pulse+sway", new[] { "pulse", "sway" })]
        [InlineData(" pulse + sway ", new[] { "pulse", "sway" })] // 多余空格要能容忍
        [InlineData("PULSE+SWAY", new[] { "pulse", "sway" })]     // 统一转小写
        [InlineData("pulse++sway", new[] { "pulse", "sway" })]    // 连续 + 号中间的空项要被丢弃，不当成一个空字符串招式
        public void SplitAnimationTypes_SplitsTrimsAndLowercases(string input, string[] expected)
        {
            Assert.Equal(expected, CustomThemeValidator.SplitAnimationTypes(input));
        }
    }
}
