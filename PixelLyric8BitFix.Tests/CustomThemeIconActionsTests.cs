using System.Linq;
using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    /// <summary>icon.actions——除了图标自己的 rows/frames（"动作 0"）之外，额外的可点击切换动作。
    /// 每个动作的 frames 走跟 icon.frames 完全一样的形状校验（ValidateFrames），用到的字符统一并进
    /// 同一份 icon.palette 核对，不需要每个动作单独配一份调色板。见 MainWindow.SkinInteractions.cs
    /// 的 CustomIcon_MouseLeftButtonDown / MainWindow.Skins.cs 的 CycleCustomIconAction。</summary>
    public class CustomThemeIconActionsTests
    {
        private static string BuildJson(string iconExtra) => $@"{{
          ""name"": ""test"",
          ""colors"": {{ ""title"": ""#FFFFFF"", ""artist"": ""#FFFFFF"", ""accent"": ""#FFFFFF"", ""lyric"": ""#FFFFFF"", ""lyricBoxBg"": ""#000000"", ""lyricBoxBorder"": ""#000000"" }},
          ""background"": {{ ""type"": ""solid"", ""stops"": [""#000000""] }},
          ""icon"": {{ ""palette"": {{ ""#"": ""#FFFFFF"", ""w"": ""#000000"" }}, ""rows"": [""####"", ""####"", ""####"", ""####""]{iconExtra} }},
          ""animation"": {{ ""type"": ""pulse"" }}
        }}";

        private const string OneWalkAction = @", ""actions"": [
            { ""name"": ""walk"", ""frames"": [[""wwww"", ""wwww"", ""wwww"", ""wwww""], [""####"", ""####"", ""####"", ""####""]] }
          ]";

        [Fact]
        public void ParseAndValidate_NoActions_IconActionsIsNull()
        {
            // 不给 actions 就是这个字段加进来之前的样子——完全向后兼容，老主题一个字都不用改
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(""));
            Assert.NotNull(theme);
            Assert.Empty(errors);
            Assert.Null(theme!.Icon!.Actions);
        }

        [Fact]
        public void ParseAndValidate_OneValidAction_NoError()
        {
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(OneWalkAction));
            Assert.NotNull(theme);
            Assert.Empty(errors);
            Assert.Single(theme!.Icon!.Actions!);
            Assert.Equal("walk", theme.Icon.Actions![0].Name);
            Assert.Equal(2, theme.Icon.Actions[0].Frames!.Count);
        }

        [Fact]
        public void ParseAndValidate_MultipleActions_AllParsed()
        {
            string actions = @", ""actions"": [
                { ""name"": ""walk"", ""frames"": [[""wwww"", ""wwww"", ""wwww"", ""wwww""]] },
                { ""name"": ""wave"", ""frames"": [[""####"", ""####"", ""####"", ""####""], [""wwww"", ""wwww"", ""wwww"", ""wwww""]] }
              ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(actions));
            Assert.NotNull(theme);
            Assert.Empty(errors);
            Assert.Equal(2, theme!.Icon!.Actions!.Count);
            Assert.Equal("wave", theme.Icon.Actions[1].Name);
        }

        [Fact]
        public void ParseAndValidate_ActionWithEmptyFrames_ReportsError()
        {
            string actions = @", ""actions"": [ { ""name"": ""walk"", ""frames"": [] } ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(actions));
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("icon.actions[0].frames") && e.Contains("至少要有 1 帧"));
        }

        [Fact]
        public void ParseAndValidate_ActionWithNoFramesField_ReportsError()
        {
            string actions = @", ""actions"": [ { ""name"": ""walk"" } ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(actions));
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("icon.actions[0].frames") && e.Contains("没填"));
        }

        [Fact]
        public void ParseAndValidate_ActionFrameSizeMismatch_ReportsError()
        {
            // 复用的是 icon.frames 同一套 ValidateFrames——这里只抽查一个形状错误的例子，
            // 完整的形状校验矩阵已经在 CustomThemeFramesTests 里覆盖过，不用重复
            string actions = @", ""actions"": [ { ""frames"": [
                [""wwww"", ""wwww"", ""wwww"", ""wwww""],
                [""wwwww"", ""wwwww"", ""wwwww"", ""wwwww"", ""wwwww""]
              ] } ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(actions));
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("icon.actions[0].frames[1]") && e.Contains("不一样"));
        }

        [Fact]
        public void ParseAndValidate_ActionUsesCharNotInPalette_ReportsError()
        {
            // 字符用没用到调色板是把 rows/frames/actions 摊平在一起核对的（跟现有 rows-vs-frames 报错
            // 归到同一个字段名是同一套粗粒度做法，见 CustomThemeValidator.ValidateIcon 里
            // usingFrames ? framesField : rowsField 那行）——这里只确认真的报了错、报的是这个字符，
            // 不要求报错信息精确指到是哪个 action 引入的
            string actions = @", ""actions"": [ { ""frames"": [[""zzzz"", ""zzzz"", ""zzzz"", ""zzzz""]] } ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(actions));
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("'z'") && e.Contains("没有给它配颜色"));
        }

        [Fact]
        public void ParseAndValidate_ActionCharAlreadyInIconPalette_NoError()
        {
            // 'w' 只在动作的帧里用到（icon.rows 本身没用），但 icon.palette 里配了颜色——
            // 校验应该是把 rows/frames/actions 用到的字符全部摊平一起核对同一份 palette
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(OneWalkAction));
            Assert.NotNull(theme);
            Assert.Empty(errors);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void ParseAndValidate_ActionNonPositiveFrameDuration_ReportsError(double duration)
        {
            string actions = $@", ""actions"": [ {{ ""frames"": [[""wwww"", ""wwww"", ""wwww"", ""wwww""]], ""frameDuration"": {duration} }} ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(actions));
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("icon.actions[0].frameDuration"));
        }

        [Fact]
        public void ParseAndValidate_ActionPositiveFrameDuration_NoError()
        {
            string actions = @", ""actions"": [ { ""frames"": [[""wwww"", ""wwww"", ""wwww"", ""wwww""]], ""frameDuration"": 0.5 } ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(actions));
            Assert.NotNull(theme);
            Assert.Empty(errors);
            Assert.Equal(0.5, theme!.Icon!.Actions![0].FrameDuration);
        }

        [Fact]
        public void ParseAndValidate_TooManyActions_ReportsError()
        {
            var actionEntries = Enumerable.Range(0, CustomThemeValidator.MaxIconActions + 1)
                .Select(_ => @"{ ""frames"": [[""wwww"", ""wwww"", ""wwww"", ""wwww""]] }");
            string actions = $@", ""actions"": [{string.Join(",\n", actionEntries)}]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(actions));
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("icon.actions") && e.Contains("最多只能有"));
        }

        [Fact]
        public void ParseAndValidate_ExactlyMaxActions_NoError()
        {
            var actionEntries = Enumerable.Range(0, CustomThemeValidator.MaxIconActions)
                .Select(_ => @"{ ""frames"": [[""wwww"", ""wwww"", ""wwww"", ""wwww""]] }");
            string actions = $@", ""actions"": [{string.Join(",\n", actionEntries)}]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(actions));
            Assert.NotNull(theme);
            Assert.Empty(errors);
            Assert.Equal(CustomThemeValidator.MaxIconActions, theme!.Icon!.Actions!.Count);
        }

        [Fact]
        public void BuildCustomIconFrames_ForAnAction_ReusesSharedPalette()
        {
            // 模拟 MainWindow.Skins.cs 的 CycleCustomIconAction：借用同一份 icon.palette、把 Frames
            // 换成某个动作的，丢给 BuildCustomIconFrames——这个方法本来就是纯数据转位图，不用改一行
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(OneWalkAction));
            Assert.Empty(errors);

            var action = theme!.Icon!.Actions![0];
            var actionIcon = new CustomThemeIcon { Palette = theme.Icon.Palette, Frames = action.Frames };
            var bitmaps = CustomThemeColorInterop.BuildCustomIconFrames(actionIcon);

            Assert.Equal(2, bitmaps.Length);
        }
    }
}
