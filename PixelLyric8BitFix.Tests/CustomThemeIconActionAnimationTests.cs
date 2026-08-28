using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    /// <summary>icon.actions[i].animation——每个动作可选的、自己独立的移动方式（不填就沿用 icon 顶层
    /// 的 animation，是这个字段加进来之前唯一的行为）。跟 CustomThemeIconActionsTests 分开是因为这个
    /// 子字段有自己独立的一套规则：drift/fall 一律不许出现（不管组合），以及"顶层已经是 drift/fall 的话
    /// 任何动作都不能再单独指定 animation"这条交叉检查。见 MainWindow.Skins.cs 的 CycleCustomIconAction。</summary>
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
        public void ParseAndValidate_ActionAnimation_AllowedTypes_NoError(string actionAnimType)
        {
            string actions = $@", ""actions"": [ {{ ""frames"": [[""wwww"", ""wwww"", ""wwww"", ""wwww""]], ""animation"": {{ ""type"": ""{actionAnimType}"" }} }} ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson("pulse", actions));
            Assert.NotNull(theme);
            Assert.Empty(errors);
            Assert.Equal(actionAnimType, theme!.Icon!.Actions![0].Animation!.Type);
        }

        [Theory]
        [InlineData("drift")]
        [InlineData("fall")]
        [InlineData("drift+pulse")]
        [InlineData("pulse+fall")]
        public void ParseAndValidate_ActionAnimation_DriftOrFall_ReportsError(string actionAnimType)
        {
            // drift/fall 在动作级别一律不许出现，不管是单独用还是组合——那两招需要图标整个活在应用
            // 主题时才搭好的专属飘过/飘落轨道里，不是点一下切动作就能随时搬进搬出的东西
            string actions = $@", ""actions"": [ {{ ""frames"": [[""wwww"", ""wwww"", ""wwww"", ""wwww""]], ""animation"": {{ ""type"": ""{actionAnimType}"" }} }} ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson("pulse", actions));
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("icon.actions[0].animation.type") && e.Contains("drift/fall"));
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
        public void ParseAndValidate_TopLevelDriftOrFall_AnyActionAnimation_ReportsError(string topLevelType)
        {
            // 顶层已经选了 drift/fall——图标已经活在专属轨道里，这时候哪怕动作自己想要的 animation
            // 本身合法（比如 "bob"），也不能再单独指定
            string actions = @", ""actions"": [ { ""frames"": [[""wwww"", ""wwww"", ""wwww"", ""wwww""]], ""animation"": { ""type"": ""bob"" } } ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(topLevelType, actions));
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("icon.actions[0].animation") && e.Contains(topLevelType));
        }

        [Theory]
        [InlineData("drift")]
        [InlineData("fall")]
        public void ParseAndValidate_TopLevelDriftOrFall_ActionWithoutAnimation_NoError(string topLevelType)
        {
            // 顶层 drift/fall + 动作完全不指定 animation（沿用顶层）——这是允许的组合，只有
            // "动作自己也想指定一份"才会被挡
            string actions = @", ""actions"": [ { ""frames"": [[""wwww"", ""wwww"", ""wwww"", ""wwww""]] } ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(topLevelType, actions));
            Assert.NotNull(theme);
            Assert.Empty(errors);
        }
    }
}
