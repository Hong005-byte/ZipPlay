using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    /// <summary>icon.actions[i].animation——每个动作可选的、自己独立的移动方式（不填就沿用 icon 顶层
    /// 的 animation，是这个字段加进来之前唯一的行为）。跟 CustomThemeIconActionsTests 分开是因为这个
    /// 子字段有自己独立的一套规则：drift/fall 可以选（点到这个动作会真的让图标搬进专属的飘过/飘落
    /// 轨道），但不能跟别的招式组合——跟顶层 animation 的组合规则完全一样。见 MainWindow.Skins.cs 的
    /// ApplyCustomIconMovement。</summary>
    public class CustomThemeIconActionAnimationTests
    {
        private static string BuildJson(string topLevelAnimationType, string iconExtra) => $@"{{
          ""name"": ""test"",
          ""colors"": {{ ""title"": ""#FFFFFF"", ""artist"": ""#FFFFFF"", ""accent"": ""#FFFFFF"", ""lyric"": ""#FFFFFF"", ""lyricBoxBg"": ""#000000"", ""lyricBoxBorder"": ""#000000"" }},
          ""background"": {{ ""type"": ""solid"", ""stops"": [""#000000""] }},
          ""icon"": {{ ""palette"": {{ ""#"": ""#FFFFFF"", ""w"": ""#000000"" }}, ""rows"": [""####"", ""####"", ""####"", ""####""]{iconExtra} }},
          ""animation"": {{ ""type"": ""{topLevelAnimationType}"" }}
        }}";

        [Fact]
        public void ParseAndValidate_ActionWithNoAnimation_IsNull()
        {
            // 不填就是这个字段加进来之前的行为——沿用 icon 顶层的 animation，向后兼容
            string actions = @", ""actions"": [ { ""frames"": [[""wwww"", ""wwww"", ""wwww"", ""wwww""]] } ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson("pulse", actions));
            Assert.NotNull(theme);
            Assert.Empty(errors);
            Assert.Null(theme!.Icon!.Actions![0].Animation);
        }

        [Theory]
        [InlineData("pulse")]
        [InlineData("twinkle")]
        [InlineData("bob")]
        [InlineData("sway")]
        [InlineData("spin")]
        [InlineData("flicker")]
        [InlineData("bob+sway")]
        [InlineData("drift")]
        [InlineData("fall")]
        public void ParseAndValidate_ActionAnimation_AllowedTypes_NoError(string actionAnimType)
        {
            // drift/fall 单独使用是允许的——点到这个动作会让图标搬进专属的飘过/飘落轨道
            string actions = $@", ""actions"": [ {{ ""frames"": [[""wwww"", ""wwww"", ""wwww"", ""wwww""]], ""animation"": {{ ""type"": ""{actionAnimType}"" }} }} ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson("pulse", actions));
            Assert.NotNull(theme);
            Assert.Empty(errors);
            Assert.Equal(actionAnimType, theme!.Icon!.Actions![0].Animation!.Type);
        }

        [Theory]
        [InlineData("drift+pulse")]
        [InlineData("pulse+fall")]
        [InlineData("drift+fall")]
        public void ParseAndValidate_ActionAnimation_DriftOrFallCombinedWithOthers_ReportsError(string actionAnimType)
        {
            // drift/fall 不能跟别的招式组合（也不能互相组合）——跟顶层 animation 的组合规则一模一样，
            // 这两招各自是整张卡片飘过/飘落的专属轨道，只能单独出现
            string actions = $@", ""actions"": [ {{ ""frames"": [[""wwww"", ""wwww"", ""wwww"", ""wwww""]], ""animation"": {{ ""type"": ""{actionAnimType}"" }} }} ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson("pulse", actions));
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("icon.actions[0].animation.type") && e.Contains("drift/fall") && e.Contains("组合"));
        }

        [Fact]
        public void ParseAndValidate_ActionAnimationMissingType_ReportsError()
        {
            // action.animation 一旦出现就是一份完整独立的配置，跟 layers[i].animation 同一套规则——
            // type 必填，不是"给个空对象就当没填"
            string actions = @", ""actions"": [ { ""frames"": [[""wwww"", ""wwww"", ""wwww"", ""wwww""]], ""animation"": {} } ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson("pulse", actions));
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("icon.actions[0].animation.type") && e.Contains("没填"));
        }

        [Fact]
        public void ParseAndValidate_ActionAnimationNonPositiveDuration_ReportsError()
        {
            string actions = @", ""actions"": [ { ""frames"": [[""wwww"", ""wwww"", ""wwww"", ""wwww""]], ""animation"": { ""type"": ""bob"", ""duration"": 0 } } ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson("pulse", actions));
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("icon.actions[0].animation.duration"));
        }

        [Theory]
        [InlineData("drift")]
        [InlineData("fall")]
        public void ParseAndValidate_TopLevelDriftOrFall_ActionWithOwnAnimation_NoError(string topLevelType)
        {
            // 顶层已经选了 drift/fall，动作自己再单独指定一份不一样的 animation（比如 "bob"）——
            // 现在允许：点到这个动作，图标会从顶层那条专属轨道搬回普通装饰栏，见
            // MainWindow.Skins.cs 的 ApplyCustomIconMovement，每次切动作都会重新决定该待在哪条轨道
            string actions = @", ""actions"": [ { ""frames"": [[""wwww"", ""wwww"", ""wwww"", ""wwww""]], ""animation"": { ""type"": ""bob"" } } ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(topLevelType, actions));
            Assert.NotNull(theme);
            Assert.Empty(errors);
            Assert.Equal("bob", theme!.Icon!.Actions![0].Animation!.Type);
        }

        [Theory]
        [InlineData("drift")]
        [InlineData("fall")]
        public void ParseAndValidate_TopLevelDriftOrFall_ActionWithoutAnimation_NoError(string topLevelType)
        {
            // 顶层 drift/fall + 动作完全不指定 animation（沿用顶层）——一直都允许，这个字段加进来
            // 之前唯一的行为
            string actions = @", ""actions"": [ { ""frames"": [[""wwww"", ""wwww"", ""wwww"", ""wwww""]] } ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(topLevelType, actions));
            Assert.NotNull(theme);
            Assert.Empty(errors);
        }
    }
}
