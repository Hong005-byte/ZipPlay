using System.Linq;
using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    /// <summary>自定义主题主图标的逐帧动画（icon.frames + icon.frameDuration）——跟走路/扇翅膀那种
    /// "几张图交替出现看起来在动"的效果同一套机制（跟 Minecraft 皮肤 Steve 走路换腿一样），
    /// 只是帧数据从用户 JSON 里读，帧数不设上限。</summary>
    public class CustomThemeFramesTests
    {
        private static string BuildJson(string iconExtra) => $@"{{
          ""name"": ""test"",
          ""colors"": {{ ""title"": ""#FFFFFF"", ""artist"": ""#FFFFFF"", ""accent"": ""#FFFFFF"", ""lyric"": ""#FFFFFF"", ""lyricBoxBg"": ""#000000"", ""lyricBoxBorder"": ""#000000"" }},
          ""background"": {{ ""type"": ""solid"", ""stops"": [""#000000""] }},
          ""icon"": {{ ""palette"": {{ ""#"": ""#FFFFFF"", ""w"": ""#000000"" }}{iconExtra} }},
          ""animation"": {{ ""type"": ""pulse"" }}
        }}";

        private const string TwoValidFrames = @", ""frames"": [
            [""####"", ""####"", ""####"", ""####""],
            [""wwww"", ""wwww"", ""wwww"", ""wwww""]
          ]";

        [Fact]
        public void ParseAndValidate_TwoFramesSameSize_NoError()
        {
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(TwoValidFrames));
            Assert.NotNull(theme);
            Assert.Empty(errors);
            Assert.Equal(2, theme!.Icon!.Frames!.Count);
        }

        [Fact]
        public void ParseAndValidate_FramesOmitted_FallsBackToRows_NoError()
        {
            // frames 不给就还是老的单帧路径，这个字段加进来之前唯一的行为，老主题不用改
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(@", ""rows"": [""####"", ""####"", ""####"", ""####""]"));
            Assert.NotNull(theme);
            Assert.Empty(errors);
            Assert.Null(theme!.Icon!.Frames);
        }

        [Fact]
        public void ParseAndValidate_FramesGiven_RowsNotRequired()
        {
            // 有 frames 的话 rows 不用填也不该报"缺字段"——渲染只认 frames
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(TwoValidFrames));
            Assert.NotNull(theme);
            Assert.DoesNotContain(errors, e => e.Contains("icon.rows"));
        }

        [Fact]
        public void ParseAndValidate_EmptyFramesArray_ReportsError()
        {
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(@", ""frames"": []"));
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("icon.frames") && e.Contains("至少要有 1 帧"));
        }

        [Fact]
        public void ParseAndValidate_FrameSizeDiffersFromFirstFrame_ReportsError()
        {
            string frames = @", ""frames"": [
                [""####"", ""####"", ""####"", ""####""],
                [""#####"", ""#####"", ""#####"", ""#####"", ""#####""]
              ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(frames));
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("icon.frames[1]") && e.Contains("不一样"));
        }

        [Fact]
        public void ParseAndValidate_FrameBelowMinSize_ReportsError()
        {
            string frames = @", ""frames"": [
                [""##"", ""##""]
              ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(frames));
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("icon.frames[0]"));
        }

        [Fact]
        public void ParseAndValidate_FrameWithMismatchedRowWidths_ReportsError()
        {
            string frames = @", ""frames"": [
                [""####"", ""###"", ""####"", ""####""]
              ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(frames));
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("icon.frames[0]") && e.Contains("不一致"));
        }

        [Fact]
        public void ParseAndValidate_CharOnlyUsedInSecondFrame_ChecksPaletteAcrossAllFrames()
        {
            // 'w' 只在第 2 帧用到，palette 里也确实配了颜色，不该报错——验证调色板校验是摊平所有帧一起查，
            // 不是只查第一帧
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(TwoValidFrames));
            Assert.NotNull(theme);
            Assert.Empty(errors);
        }

        [Fact]
        public void ParseAndValidate_CharInFrameWithoutPaletteEntry_ReportsError()
        {
            string frames = @", ""frames"": [
                [""####"", ""####"", ""####"", ""####""],
                [""zzzz"", ""zzzz"", ""zzzz"", ""zzzz""]
              ]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(frames));
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("icon.frames") && e.Contains("'z'"));
        }

        [Fact]
        public void ParseAndValidate_NoFrameCountLimit_ManyFramesStillValidates()
        {
            // 帧数不设上限——50 帧（明显超过任何"看起来合理"的走路循环）照样应该能过校验，
            // 校验只保证形状一致，不对数量本身设门槛
            var frameEntries = Enumerable.Range(0, 50).Select(_ => @"[""####"", ""####"", ""####"", ""####""]");
            string frames = $@", ""frames"": [{string.Join(",\n", frameEntries)}]";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(frames));
            Assert.NotNull(theme);
            Assert.Empty(errors);
            Assert.Equal(50, theme!.Icon!.Frames!.Count);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void ParseAndValidate_NonPositiveFrameDuration_ReportsError(double duration)
        {
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(TwoValidFrames + $@", ""frameDuration"": {duration}"));
            Assert.Null(theme);
            Assert.Contains(errors, e => e.Contains("icon.frameDuration"));
        }

        [Fact]
        public void ParseAndValidate_PositiveFrameDuration_NoError()
        {
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(TwoValidFrames + @", ""frameDuration"": 0.4"));
            Assert.NotNull(theme);
            Assert.Empty(errors);
            Assert.Equal(0.4, theme!.Icon!.FrameDuration);
        }

        [Fact]
        public void BuildCustomIconFrames_WithFrames_ReturnsOneBitmapPerFrame()
        {
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(TwoValidFrames));
            Assert.Empty(errors);
            var bitmaps = CustomThemeColorInterop.BuildCustomIconFrames(theme!.Icon!);
            Assert.Equal(2, bitmaps.Length);
        }

        [Fact]
        public void BuildCustomIconFrames_WithoutFrames_ReturnsSingleBitmap()
        {
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(BuildJson(@", ""rows"": [""####"", ""####"", ""####"", ""####""]"));
            Assert.Empty(errors);
            var bitmaps = CustomThemeColorInterop.BuildCustomIconFrames(theme!.Icon!);
            Assert.Single(bitmaps);
        }

        [Fact]
        public void GetFrameDurationSeconds_Omitted_ReturnsDefault()
        {
            var icon = new CustomThemeIcon();
            Assert.Equal(CustomThemeValidator.DefaultFrameDurationSeconds, CustomThemeValidator.GetFrameDurationSeconds(icon));
        }

        [Fact]
        public void GetFrameDurationSeconds_Given_ReturnsGivenValue()
        {
            var icon = new CustomThemeIcon { FrameDuration = 0.6 };
            Assert.Equal(0.6, CustomThemeValidator.GetFrameDurationSeconds(icon));
        }

        // ── 回归测试：MainWindow.BuildSkinThemeFromCustom 之前无脑读 custom.Icon.Rows!，选了一份只写了
        // frames、没写 rows 的自定义主题当皮肤时，Mini 小方块/进度条拖拽图标/分享卡片这几处会直接
        // NullReferenceException——一进播放器就崩。见 MainWindow.Skins.cs 的注释。 ──

        private static CustomTheme BuildThemeWithFramesOnly()
        {
            const string json = @"{
              ""name"": ""帧测试"",
              ""colors"": { ""title"": ""#FFFFFF"", ""artist"": ""#FFFFFF"", ""accent"": ""#FFFFFF"", ""lyric"": ""#FFFFFF"", ""lyricBoxBg"": ""#000000"", ""lyricBoxBorder"": ""#000000"" },
              ""background"": { ""type"": ""solid"", ""stops"": [""#000000""] },
              ""icon"": { ""palette"": { ""#"": ""#FFFFFF"" }, ""frames"": [[""####"", ""####"", ""####"", ""####""], [""####"", ""####"", ""####"", ""####""]] },
              ""animation"": { ""type"": ""pulse"" }
            }";
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(json);
            Assert.Empty(errors);
            return theme!;
        }

        [Fact]
        public void BuildSkinThemeFromCustom_IconUsesFramesOnly_DoesNotThrow()
        {
            var theme = BuildThemeWithFramesOnly();
            Assert.Null(theme.Icon!.Rows); // 确认这份测试数据真的没有 rows，测的就是这条路径

            var skinTheme = MainWindow.BuildSkinThemeFromCustom(theme);

            // MiniIcon 是个延迟求值的 Func——之前的 bug 正是在真的调用它（Mini 模式/进度条/分享卡片
            // 触发时）才会炸，这里模拟那一刻的调用，确认不再抛 NullReferenceException
            var exception = Record.Exception(() => skinTheme.MiniIcon());
            Assert.Null(exception);
        }
    }
}
