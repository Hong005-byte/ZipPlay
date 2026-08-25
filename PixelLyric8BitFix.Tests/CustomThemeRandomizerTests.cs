using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    /// <summary>"🎲 随机生成一份"按钮背后的 CustomThemeRandomizer.GenerateJson()——每次抽的调色板/图标/
    /// 动画招式/渐变还是纯色都是随机组合的，这里反复抽很多次，保证不管抽到哪种组合，吐出来的 JSON
    /// 都能直接过 CustomThemeValidator 的校验（不会出现"漏了某个必填字段"或者"某个组合分支拼错格式"
    /// 这种要抽到特定随机结果才会暴露的问题）。</summary>
    public class CustomThemeRandomizerTests
    {
        [Fact]
        public void GenerateJson_ManyTimes_AlwaysPassesValidation()
        {
            for (int i = 0; i < 200; i++)
            {
                string json = CustomThemeRandomizer.GenerateJson();
                var (theme, errors) = CustomThemeValidator.ParseAndValidate(json);

                Assert.True(errors.Count == 0, $"第 {i} 次抽到的随机主题没能过校验：{string.Join(" | ", errors)}\nJSON:\n{json}");
                Assert.NotNull(theme);
            }
        }

        [Fact]
        public void GenerateJson_WithExclusivePool_AlwaysPassesValidation()
        {
            // includeExclusive=true 时随机池多了"祖母绿荣光"那份限定调色板——单独跑一遍，
            // 防的是"这份限定调色板本身有个字段忘填/格式错了"这种只有抽中它才会暴露的问题
            for (int i = 0; i < 200; i++)
            {
                string json = CustomThemeRandomizer.GenerateJson(includeExclusive: true);
                var (theme, errors) = CustomThemeValidator.ParseAndValidate(json);
                Assert.True(errors.Count == 0, $"第 {i} 次（含限定调色板）没能过校验：{string.Join(" | ", errors)}\nJSON:\n{json}");
                Assert.NotNull(theme);
            }
        }

        [Fact]
        public void GenerateJson_WithExclusivePool_EventuallyPicksExclusivePalette()
        {
            // 限定调色板占池子里 9 份中的 1 份，200 次里完全抽不到的概率是 (8/9)^200，小到可以忽略——
            // 断言"至少出现过一次"，顺带验证 includeExclusive=true 真的把它加进池子了，不是传了个没用的参数
            bool sawExclusive = false;
            for (int i = 0; i < 200 && !sawExclusive; i++)
            {
                string json = CustomThemeRandomizer.GenerateJson(includeExclusive: true);
                if (json.Contains("祖母绿荣光")) sawExclusive = true;
            }
            Assert.True(sawExclusive, "跑了 200 次 includeExclusive=true，一次都没抽到限定调色板，概率上不太正常，查一下是不是没真的加进池子里");
        }

        [Fact]
        public void GenerateJson_WithoutExclusivePool_NeverPicksExclusivePalette()
        {
            // 默认 includeExclusive=false（对应"主题工匠"没解锁）——限定调色板不该出现在池子里
            for (int i = 0; i < 200; i++)
            {
                string json = CustomThemeRandomizer.GenerateJson();
                Assert.DoesNotContain("祖母绿荣光", json);
            }
        }

        [Fact]
        public void GenerateJson_SometimesIncludesLayers_AndTheyAlwaysValidate()
        {
            // 4 成概率带一个额外层——200 次里断言"至少出现过一次带 layers 的"，顺带在每次出现时确认
            // 那份 layers 本身也过校验（防的是"主图标没问题，layers 那段格式拼错了"这种只挑对分支才暴露的错）
            bool sawLayers = false;
            for (int i = 0; i < 200; i++)
            {
                string json = CustomThemeRandomizer.GenerateJson();
                var (theme, errors) = CustomThemeValidator.ParseAndValidate(json);
                Assert.True(errors.Count == 0, $"第 {i} 次没能过校验：{string.Join(" | ", errors)}\nJSON:\n{json}");
                if (theme!.Layers is { Count: > 0 }) sawLayers = true;
            }
            Assert.True(sawLayers, "跑了 200 次，一次都没抽到带 layers 的结果，概率上不太正常，查一下是不是没真的接上");
        }

        [Fact]
        public void GenerateJson_IconRows_AreEqualWidth()
        {
            // 随机图标形状是手写的字符网格，最容易犯的错就是某一行敲少/敲多一个字符——
            // 校验本身会拦，但这里单独断言一下，出问题时报错信息能直接定位到"图标网格宽度不齐"，
            // 而不是淹没在上面那个更笼统的"随便抽 200 次总有能过的"测试里
            for (int i = 0; i < 50; i++)
            {
                var (theme, errors) = CustomThemeValidator.ParseAndValidate(CustomThemeRandomizer.GenerateJson());
                Assert.NotNull(theme);
                Assert.DoesNotContain(errors, e => e.Contains("icon.rows"));
            }
        }
    }
}
