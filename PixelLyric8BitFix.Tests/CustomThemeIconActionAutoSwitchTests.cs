using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    /// <summary>icon.actions[i].autoSwitchAfterSeconds——数据驱动的自动切换阈值（当前歌曲连续播放满
    /// 这么多秒，不用点击就自动切到这个动作）。校验层只管"是不是正数"这一件事，挑阈值/往前推不回头
    /// 那套逻辑是运行时（MainWindow.Skins.cs 的 EvaluateAutoSwitchIconAction）的事，不在这里测——
    /// 那部分依赖 MainWindow 的播放状态，见 MainWindow.ListeningStats.cs 里
    /// _customIconContinuousTrackSeconds 的说明。</summary>
    public class CustomThemeIconActionAutoSwitchTests
    {
        private static string BuildJson(string iconExtra) => $@"{{
          ""name"": ""test"",
          ""colors"": {{ ""title"": ""#FFFFFF"", ""artist"": ""#FFFFFF"", ""accent"": ""#FFFFFF"", ""lyric"": ""#FFFFFF"", ""lyricBoxBg"": ""#000000"", ""lyricBoxBorder"": ""#000000"" }},
          ""background"": {{ ""type"": ""solid"", ""stops"": [""#000000""] }},
          ""icon"": {{ ""palette"": {{ ""#"": ""#FFFFFF"" }}, ""rows"": [""####"", ""####"", ""####"", ""####""]{iconExtra} }},
          ""animation"": {{ ""type"": ""pulse"" }}
        }}";

        [Fact]
        public void ParseAndValidate_ActionWithNoAutoSwitch_IsNull()
        {
            // 不填就是这个字段加进来之前的行为——完全靠点击手动切换，向后兼容
            string actions = @", ""actions"": [ { ""frames"": [[""####"", ""####"", ""####"", ""####""]] } ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(actions));
            Assert.NotNull(theme);
            Assert.Empty(errors);
            Assert.Null(theme!.Icon!.Actions![0].AutoSwitchAfterSeconds);
        }

        [Fact]
        public void ParseAndValidate_ActionWithPositiveAutoSwitch_NoError()
        {
            string actions = @", ""actions"": [ { ""frames"": [[""####"", ""####"", ""####"", ""####""]], ""autoSwitchAfterSeconds"": 120 } ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(actions));
            Assert.NotNull(theme);
            Assert.Empty(errors);
            Assert.Equal(120, theme!.Icon!.Actions![0].AutoSwitchAfterSeconds);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public void ParseAndValidate_ActionNonPositiveAutoSwitch_ReportsError(double seconds)
        {
            string actions = $@", ""actions"": [ {{ ""frames"": [[""####"", ""####"", ""####"", ""####""]], ""autoSwitchAfterSeconds"": {seconds} }} ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(actions));
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("icon.actions[0].autoSwitchAfterSeconds"));
        }

        [Fact]
        public void ParseAndValidate_MultipleActionsWithDifferentThresholds_AllParsed()
        {
            // 阶段递进的典型用法：第一个动作 60 秒后触发，第二个 120 秒后触发
            string actions = @", ""actions"": [
                { ""name"": ""半程"", ""frames"": [[""####"", ""####"", ""####"", ""####""]], ""autoSwitchAfterSeconds"": 60 },
                { ""name"": ""投入"", ""frames"": [[""####"", ""####"", ""####"", ""####""]], ""autoSwitchAfterSeconds"": 120 }
              ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(actions));
            Assert.NotNull(theme);
            Assert.Empty(errors);
            Assert.Equal(60, theme!.Icon!.Actions![0].AutoSwitchAfterSeconds);
            Assert.Equal(120, theme.Icon.Actions[1].AutoSwitchAfterSeconds);
        }
    }
}
