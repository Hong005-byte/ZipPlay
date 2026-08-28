using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    /// <summary>"📄 下载详细说明"文档——主要防的是字符串拼接时手滑漏了个引号/大括号，导致嵌入的
    /// 示例 JSON 实际上不是合法 JSON 了（读者/AI 照着抄出来的东西也会跟着坏掉），或者关键章节标题
    /// 被误删。</summary>
    public class CustomThemeSpecDocTests
    {
        [Fact]
        public void Build_ContainsAllExpectedSectionHeaders()
        {
            string doc = CustomThemeSpecDoc.Build();

            Assert.Contains("## 顶层字段", doc);
            Assert.Contains("## 颜色 colors", doc);
            Assert.Contains("## 背景 background", doc);
            Assert.Contains("## 图标 icon", doc);
            Assert.Contains("## 动画 animation", doc);
            Assert.Contains("## 额外装饰层 layers", doc);
            Assert.Contains("## 成就联动", doc);
            Assert.Contains("## 完整示例", doc);
            Assert.Contains("## 给 AI 的提示词模板", doc);
        }

        [Fact]
        public void Build_EmbeddedExample_IsSameAsCustomThemeExample_AndPassesValidation()
        {
            string doc = CustomThemeSpecDoc.Build();

            // 示例 JSON 本身必须原样出现在文档里（不是被截断或者转义坏了）
            Assert.Contains(CustomThemeExample.Json, doc);

            var (theme, errors) = CustomThemeValidator.ParseAndValidate(CustomThemeExample.Json);
            Assert.NotNull(theme);
            Assert.Empty(errors);
        }

        [Fact]
        public void Build_MentionsActions()
        {
            // icon.actions（画板"动作"选择器对应的那个字段）在这份文档加进来之前完全没被提到过——
            // 一个只看这份文档（或者把它丢给 AI）的人/AI 根本不知道这个字段存在。这里只做粗粒度检查
            // （标题+关键字段名都出现），详细的形状规则由 CustomThemeIconActionsTests 覆盖。
            string doc = CustomThemeSpecDoc.Build();

            Assert.Contains("actions", doc);
            Assert.Contains("frameDuration", doc);
        }

        [Fact]
        public void Build_ActionsExampleSnippet_IsValidAndPassesValidation()
        {
            // "actions" 小节里手写的那份示例 icon 片段——独立摘出来塞进一份完整主题 JSON，
            // 确认它真的是合法 JSON、而且真的能过校验，不是一份看着像但其实抄错了逗号/尺寸的示例
            // （文档专门声明是要给 AI 读的，示例本身要是坏的，AI 照着抄出来的东西也会跟着坏）
            const string iconSnippet = @"{
  ""palette"": { ""#"": ""#F9C784"" },
  ""rows"": [
    ""....."",
    "".###."",
    ""..#.."",
    "".#.#.""
  ],
  ""actions"": [
    {
      ""name"": ""挥手"",
      ""frames"": [
        [""#...."", "".###."", ""..#.."", "".#.#.""],
        [""....#"", "".###."", ""..#.."", "".#.#.""]
      ],
      ""frameDuration"": 0.2,
      ""animation"": { ""type"": ""spin"", ""duration"": 1.5 }
    },
    {
      ""name"": ""投入"",
      ""frames"": [[""#...."", "".###."", ""..#.."", "".#.#.""]],
      ""autoSwitchAfterSeconds"": 120
    }
  ]
}";
            Assert.Contains(iconSnippet, CustomThemeSpecDoc.Build());

            // 示例叙述里顶层是 "sway"（""默认站姿是 sway 轻摆""）——action.animation 是 "spin"，
            // 两个都不是 drift/fall，这个组合应该完全合法
            string themeJson = $@"{{
              ""name"": ""test"",
              ""colors"": {{ ""title"": ""#FFFFFF"", ""artist"": ""#FFFFFF"", ""accent"": ""#FFFFFF"", ""lyric"": ""#FFFFFF"", ""lyricBoxBg"": ""#000000"", ""lyricBoxBorder"": ""#000000"" }},
              ""background"": {{ ""type"": ""solid"", ""stops"": [""#000000""] }},
              ""icon"": {iconSnippet},
              ""animation"": {{ ""type"": ""sway"" }}
            }}";

            var (theme, errors) = CustomThemeValidator.ParseAndValidate(themeJson);
            Assert.True(errors.Count == 0, string.Join(" | ", errors));
            Assert.NotNull(theme);
            Assert.Equal("spin", theme!.Icon!.Actions![0].Animation!.Type);
            Assert.Equal(120, theme.Icon.Actions[1].AutoSwitchAfterSeconds);
        }

        [Fact]
        public void Build_MentionsActionAnimation()
        {
            // icon.actions[i].animation（动作专属的移动方式）也是一个只看这份文档/丢给 AI 读的人
            // 完全没法知道存在的字段，除非写进去——粗粒度检查关键词，详细规则由
            // CustomThemeIconActionAnimationTests 覆盖
            string doc = CustomThemeSpecDoc.Build();

            Assert.Contains("drift", doc);
            Assert.Contains("musicReactive", doc);
        }

        [Fact]
        public void Build_MentionsAutoSwitchAfterSeconds()
        {
            // icon.actions[i].autoSwitchAfterSeconds（数据驱动的自动切换）同理——粗粒度检查关键词，
            // 详细规则由 CustomThemeIconActionAutoSwitchTests / …SelectorTests 覆盖
            string doc = CustomThemeSpecDoc.Build();

            Assert.Contains("autoSwitchAfterSeconds", doc);
            Assert.Contains("连续播放", doc);
        }
    }
}
