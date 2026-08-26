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
    }
}
