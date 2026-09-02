using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    /// <summary>icon.actions[i].transitionSeconds——切到这个动作时的过渡淡化时长。校验层只管"是不是
    /// 正数"这一件事（跟 autoSwitchAfterSeconds/frameDuration 同一套道理），"只在同一条移动轨道才生效"
    /// 那部分是运行时（MainWindow.Skins.cs 的 SetCustomIconActionIndex/PlayCustomIconTransition）的事，
    /// 不在这里测——那部分依赖 MainWindow 的 UI 元素，见那两个方法的注释。</summary>
    public class CustomThemeIconActionTransitionTests
    {
        private static string BuildJson(string iconExtra) => $@"{{
          ""name"": ""test"",
          ""colors"": {{ ""title"": ""#FFFFFF"", ""artist"": ""#FFFFFF"", ""accent"": ""#FFFFFF"", ""lyric"": ""#FFFFFF"", ""lyricBoxBg"": ""#000000"", ""lyricBoxBorder"": ""#000000"" }},
          ""background"": {{ ""type"": ""solid"", ""stops"": [""#000000""] }},
          ""icon"": {{ ""palette"": {{ ""#"": ""#FFFFFF"" }}, ""rows"": [""####"", ""####"", ""####"", ""####""]{iconExtra} }},
          ""animation"": {{ ""type"": ""pulse"" }}
        }}";

        [Fact]
        public void ParseAndValidate_ActionWithNoTransition_IsNull()
        {
            // 不填就是这个字段加进来之前的行为——瞬间切换，向后兼容
            string actions = @", ""actions"": [ { ""frames"": [[""####"", ""####"", ""####"", ""####""]] } ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(actions));
            Assert.NotNull(theme);
            Assert.Empty(errors);
            Assert.Null(theme!.Icon!.Actions![0].TransitionSeconds);
        }

        [Fact]
        public void ParseAndValidate_ActionWithPositiveTransition_NoError()
        {
            string actions = @", ""actions"": [ { ""frames"": [[""####"", ""####"", ""####"", ""####""]], ""transitionSeconds"": 1.5 } ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(actions));
            Assert.NotNull(theme);
            Assert.Empty(errors);
            Assert.Equal(1.5, theme!.Icon!.Actions![0].TransitionSeconds);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-2)]
        public void ParseAndValidate_ActionNonPositiveTransition_ReportsError(double seconds)
        {
            string actions = $@", ""actions"": [ {{ ""frames"": [[""####"", ""####"", ""####"", ""####""]], ""transitionSeconds"": {seconds} }} ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(actions));
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("icon.actions[0].transitionSeconds"));
        }
    }
}
