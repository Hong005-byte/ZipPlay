using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    /// <summary>"🔀 混搭已存主题"背后的 CustomThemeRemixer.Remix——三份来源各摘一块拼成新主题，
    /// 吐出来的 JSON 必须能重新过 CustomThemeValidator 的校验（不然生成的草稿用户改都改不了，
    /// 直接卡在"保存"那一步报错），而且摘的字段要对得上：配色来源出 colors/background/font，
    /// 图标来源出 icon/layers，动画来源出 animation。</summary>
    public class CustomThemeRemixerTests
    {
        private static CustomTheme ParseValidTheme(string json)
        {
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(json);
            Assert.NotNull(theme);
            Assert.Empty(errors);
            return theme!;
        }

        // 手写 JSON 字符串字面量里嵌套双引号很容易转义出错（之前就在这踩过一次坑），干脆用
        // System.Text.Json/Newtonsoft 现成的对象序列化来拼测试用的图标网格，不再手打转义
        private static string BuildIconRowsJson(char iconChar)
        {
            string row = new string(iconChar, 4);
            string blank = new string('.', 4);
            return $@"[""{row}"", ""{blank}"", ""{blank}"", ""{blank}""]";
        }

        private static CustomTheme BuildTheme(string name, string accent, char iconChar, string animType, bool includeLayer)
        {
            string iconRows = BuildIconRowsJson(iconChar);
            string layersField = includeLayer
                ? $@", ""layers"": [ {{ ""anchor"": ""top-left"", ""icon"": {{ ""palette"": {{ ""{iconChar}"": ""{accent}"" }}, ""rows"": {iconRows} }}, ""animation"": {{ ""type"": ""twinkle"" }} }} ]"
                : "";
            string json = $@"{{
              ""name"": ""{name}"",
              ""font"": ""Consolas"",
              ""colors"": {{ ""title"": ""#FFFFFF"", ""artist"": ""#FFFFFF"", ""accent"": ""{accent}"", ""lyric"": ""#FFFFFF"", ""lyricBoxBg"": ""#000000"", ""lyricBoxBorder"": ""#000000"" }},
              ""background"": {{ ""type"": ""solid"", ""stops"": [""{accent}""] }},
              ""icon"": {{ ""palette"": {{ ""{iconChar}"": ""{accent}"" }}, ""rows"": {iconRows} }},
              ""animation"": {{ ""type"": ""{animType}"" }}{layersField}
            }}";
            return ParseValidTheme(json);
        }

        [Fact]
        public void Remix_OutputPassesValidation()
        {
            var colorSource = BuildTheme("配色主题", "#F9C784", '#', "pulse", includeLayer: false);
            var iconSource = BuildTheme("图标主题", "#29B6F6", 'o', "spin", includeLayer: true);
            var animationSource = BuildTheme("动画主题", "#8BC34A", 'w', "flicker", includeLayer: false);

            string json = CustomThemeRemixer.Remix(colorSource, iconSource, animationSource);
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(json);

            Assert.True(errors.Count == 0, $"混搭结果没过校验：{string.Join(" | ", errors)}\nJSON:\n{json}");
            Assert.NotNull(theme);
        }

        [Fact]
        public void Remix_TakesColorsAndBackgroundAndFontFromColorSource()
        {
            var colorSource = BuildTheme("配色主题", "#F9C784", '#', "pulse", includeLayer: false);
            var iconSource = BuildTheme("图标主题", "#29B6F6", 'o', "spin", includeLayer: false);
            var animationSource = BuildTheme("动画主题", "#8BC34A", 'w', "flicker", includeLayer: false);

            var (theme, _) = CustomThemeValidator.ParseAndValidate(CustomThemeRemixer.Remix(colorSource, iconSource, animationSource));

            Assert.Equal("#F9C784", theme!.Colors!.Accent);
            Assert.Equal("Consolas", theme.Font);
            Assert.Equal(colorSource.Background!.Stops, theme.Background!.Stops);
        }

        [Fact]
        public void Remix_TakesIconAndLayersFromIconSource()
        {
            var colorSource = BuildTheme("配色主题", "#F9C784", '#', "pulse", includeLayer: false);
            var iconSource = BuildTheme("图标主题", "#29B6F6", 'o', "spin", includeLayer: true);
            var animationSource = BuildTheme("动画主题", "#8BC34A", 'w', "flicker", includeLayer: false);

            var (theme, _) = CustomThemeValidator.ParseAndValidate(CustomThemeRemixer.Remix(colorSource, iconSource, animationSource));

            Assert.Equal("#29B6F6", theme!.Icon!.Palette!["o"]);
            Assert.NotNull(theme.Layers);
            Assert.Single(theme.Layers!);
        }

        [Fact]
        public void Remix_TakesAnimationFromAnimationSource()
        {
            var colorSource = BuildTheme("配色主题", "#F9C784", '#', "pulse", includeLayer: false);
            var iconSource = BuildTheme("图标主题", "#29B6F6", 'o', "spin", includeLayer: false);
            var animationSource = BuildTheme("动画主题", "#8BC34A", 'w', "flicker", includeLayer: false);

            var (theme, _) = CustomThemeValidator.ParseAndValidate(CustomThemeRemixer.Remix(colorSource, iconSource, animationSource));

            Assert.Equal("flicker", theme!.Animation!.Type);
        }

        [Fact]
        public void Remix_NameMentionsAllThreeSourceNames()
        {
            var colorSource = BuildTheme("暮色海岸", "#F9C784", '#', "pulse", includeLayer: false);
            var iconSource = BuildTheme("像素方块", "#29B6F6", 'o', "spin", includeLayer: false);
            var animationSource = BuildTheme("旋转吧", "#8BC34A", 'w', "flicker", includeLayer: false);

            var (theme, _) = CustomThemeValidator.ParseAndValidate(CustomThemeRemixer.Remix(colorSource, iconSource, animationSource));

            Assert.Contains("暮色海岸", theme!.Name);
            Assert.Contains("像素方块", theme.Name);
            Assert.Contains("旋转吧", theme.Name);
        }

        [Fact]
        public void Remix_AllThreeSourcesAreSameTheme_StillPassesValidation()
        {
            var theme = BuildTheme("独一份", "#F9C784", '#', "pulse", includeLayer: true);
            string json = CustomThemeRemixer.Remix(theme, theme, theme);
            var (result, errors) = CustomThemeValidator.ParseAndValidate(json);

            Assert.True(errors.Count == 0, string.Join(" | ", errors));
            Assert.NotNull(result);
        }
    }
}
