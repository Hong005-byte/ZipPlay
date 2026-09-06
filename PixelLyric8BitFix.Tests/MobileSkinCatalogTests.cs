using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    public class MobileSkinCatalogTests
    {
        [Fact]
        public void Find_KnownId_ReturnsMatchingPalette()
        {
            var palette = MobileSkinCatalog.Find("Cyberpunk");
            Assert.Equal("Cyberpunk", palette.Id);
            Assert.Equal("霓虹赛博朋克风", palette.DisplayName);
        }

        [Fact]
        public void Find_UnknownId_FallsBackToFirstPalette()
        {
            // 存的 id 是旧版本/脏数据对不上号（比如以后下线了某套皮肤，用户存档里还是旧 id）——
            // 不该直接崩，兜底回第一套
            var palette = MobileSkinCatalog.Find("这个皮肤不存在");
            Assert.Equal(MobileSkinCatalog.All[0].Id, palette.Id);
        }

        [Fact]
        public void Find_NullId_FallsBackToFirstPalette()
        {
            var palette = MobileSkinCatalog.Find(null);
            Assert.Equal(MobileSkinCatalog.All[0].Id, palette.Id);
        }

        [Fact]
        public void DefaultSkinId_ExistsInCatalog()
        {
            // DefaultSkinId 是给"从没存过设置的新用户"兜底用的，这个 id 必须真的在表里，不然
            // 每个新用户第一次打开都会静默走到 Find 的兜底分支
            var palette = MobileSkinCatalog.Find(MobileSkinCatalog.DefaultSkinId);
            Assert.Equal(MobileSkinCatalog.DefaultSkinId, palette.Id);
        }

        [Fact]
        public void All_HasNoDuplicateIds()
        {
            var ids = new HashSet<string>();
            foreach (var palette in MobileSkinCatalog.All)
            {
                Assert.True(ids.Add(palette.Id), $"重复的皮肤 id: {palette.Id}");
            }
        }

        [Fact]
        public void All_ContainsFullDesktopRosterExceptCustom()
        {
            // 阶段 6：桌面版 PlayerSkin 枚举除了 Custom（那是自定义主题的事，见 MobileCustomThemeStore）
            // 剩下的每一个都该在这张表里有一条对应数据——漏了哪个，以后想在 Mobile 上选那套皮肤
            // 就没有配色可用，这条测试就是为了在漏掉的时候第一时间报出来，不是等真机测试才发现
            string[] expectedIds =
            {
                "Minecraft", "Simple", "Crt", "Cyberpunk", "Vinyl", "Glass", "Lofi", "Aurora", "Rain",
                "Starry", "Campfire", "Sakura", "Cassette", "Cloud", "Candle", "Plant", "Sunset",
                "Arcade", "Invaders", "City", "Crown",
            };
            var actualIds = MobileSkinCatalog.All.Select(p => p.Id).ToHashSet();

            foreach (var id in expectedIds)
            {
                Assert.True(actualIds.Contains(id), $"缺了皮肤 \"{id}\" 的配色数据");
            }
        }
    }
}
