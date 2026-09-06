using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    public class MobileSkinIconCatalogTests
    {
        [Fact]
        public void Find_KnownSkinId_ReturnsIcon()
        {
            var icon = MobileSkinIconCatalog.Find("Simple");
            Assert.NotNull(icon);
            Assert.Equal(8, icon!.Value.Rows.Length);
        }

        [Fact]
        public void Find_UnknownSkinId_ReturnsNull()
        {
            Assert.Null(MobileSkinIconCatalog.Find("custom:some-file.json"));
            Assert.Null(MobileSkinIconCatalog.Find("这个皮肤不存在"));
        }

        [Fact]
        public void HasAnIconForEveryBuiltInSkinPalette()
        {
            // MobileSkinPalette 和 MobileSkinIconCatalog 是两张独立的表（见该文件顶部注释），拆开维护
            // 就有"配色加了、图标忘了加"这种漏项的风险——这条测试专门盯这个
            foreach (var palette in MobileSkinCatalog.All)
            {
                Assert.True(MobileSkinIconCatalog.BySkinId.ContainsKey(palette.Id), $"皮肤 \"{palette.Id}\" 有配色但没有图标数据");
            }
        }

        [Theory]
        [InlineData("Simple")] [InlineData("Crt")] [InlineData("Cyberpunk")] [InlineData("Vinyl")]
        [InlineData("Glass")] [InlineData("Lofi")] [InlineData("Aurora")] [InlineData("Rain")]
        [InlineData("Starry")] [InlineData("Campfire")] [InlineData("Sakura")] [InlineData("Cassette")]
        [InlineData("Cloud")] [InlineData("Candle")] [InlineData("Plant")] [InlineData("Sunset")]
        [InlineData("Arcade")] [InlineData("Invaders")] [InlineData("City")] [InlineData("Minecraft")]
        [InlineData("Crown")]
        public void EveryIcon_AllRowsSameWidth_AndOnlyUsesCharsInPalette(string skinId)
        {
            // 手抄 21 份网格数据最容易出的错就是某一行漏打/多打一个字符——宽度不齐会导致渲染错位；
            // 用了调色板里没有的字符会导致渲染那边（无论桌面版还是 Mobile）在这个字符上找不到颜色。
            // 这条测试把两种最容易犯的手抄错误都堵在这里，不用等真机截图才发现某个图标歪了
            var icon = MobileSkinIconCatalog.Find(skinId);
            Assert.NotNull(icon);

            var (rows, palette) = (icon!.Value.Rows, icon.Value.Palette);
            Assert.True(rows.Length > 0, $"{skinId}: 没有任何行");

            int width = rows[0].Length;
            for (int i = 0; i < rows.Length; i++)
            {
                Assert.True(rows[i].Length == width, $"{skinId}: 第 {i + 1} 行宽度是 {rows[i].Length}，跟第 1 行的 {width} 不一致");
                foreach (char c in rows[i])
                {
                    Assert.True(palette.ContainsKey(c), $"{skinId}: 第 {i + 1} 行用了字符 '{c}'，但调色板里没有配色");
                }
            }
        }
    }
}
