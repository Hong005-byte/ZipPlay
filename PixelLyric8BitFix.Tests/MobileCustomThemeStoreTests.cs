using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    public class MobileCustomThemeStoreTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "zipplay_custom_theme_tests_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
        }

        [Fact]
        public void ListAll_EmptyStore_ReturnsEmptyList()
        {
            var store = new MobileCustomThemeStore(_dir);
            Assert.Empty(store.ListAll());
        }

        [Fact]
        public void Save_ThenListAll_ContainsIt()
        {
            var store = new MobileCustomThemeStore(_dir);
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(MobileCustomThemeExample.Json);
            Assert.Empty(errors);

            var (success, error, fileName) = store.Save(theme!);

            Assert.True(success, error);
            Assert.NotNull(fileName);
            var all = store.ListAll();
            Assert.Single(all);
            Assert.Equal("我的海边黄昏", all[0].Theme.Name);
        }

        [Fact]
        public void Load_SavedTheme_RoundTrips()
        {
            var store = new MobileCustomThemeStore(_dir);
            var (theme, _) = CustomThemeValidator.ParseAndValidate(MobileCustomThemeExample.Json);
            var (_, _, fileName) = store.Save(theme!);

            var loaded = store.Load(fileName!);

            Assert.NotNull(loaded);
            Assert.Equal(theme!.Name, loaded!.Name);
        }

        [Fact]
        public void Load_UnknownFileName_ReturnsNull()
        {
            var store = new MobileCustomThemeStore(_dir);
            Assert.Null(store.Load("不存在的文件.json"));
        }

        [Fact]
        public void Save_AtMaxThemes_RejectsNewOne()
        {
            var store = new MobileCustomThemeStore(_dir);
            var (theme, _) = CustomThemeValidator.ParseAndValidate(MobileCustomThemeExample.Json);

            for (int i = 0; i < MobileCustomThemeStore.MaxThemes; i++)
            {
                var (success, error, _) = store.Save(theme!);
                Assert.True(success, error);
            }

            var (finalSuccess, finalError, finalFileName) = store.Save(theme!);

            Assert.False(finalSuccess);
            Assert.NotNull(finalError);
            Assert.Null(finalFileName);
            Assert.Equal(MobileCustomThemeStore.MaxThemes, store.ListAll().Count);
        }

        [Fact]
        public void Delete_RemovesTheme()
        {
            var store = new MobileCustomThemeStore(_dir);
            var (theme, _) = CustomThemeValidator.ParseAndValidate(MobileCustomThemeExample.Json);
            var (_, _, fileName) = store.Save(theme!);

            store.Delete(fileName!);

            Assert.Empty(store.ListAll());
            Assert.Null(store.Load(fileName!));
        }

        [Fact]
        public void Delete_UnknownFileName_DoesNotThrow()
        {
            var store = new MobileCustomThemeStore(_dir);
            store.Delete("不存在的文件.json");
        }

        [Fact]
        public void ListAll_CorruptedFileAmongValidOnes_SkipsOnlyTheCorruptedOne()
        {
            var store = new MobileCustomThemeStore(_dir);
            var (theme, _) = CustomThemeValidator.ParseAndValidate(MobileCustomThemeExample.Json);
            store.Save(theme!);

            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, "corrupted.json"), "这不是合法的 JSON {{{");

            Assert.Single(store.ListAll()); // 只有那份手写的坏文件被跳过，合法的那份还在
        }
    }

    public class MobileSkinCatalogFromCustomThemeTests
    {
        [Fact]
        public void FromCustomTheme_ExtractsColorsFromExample()
        {
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(MobileCustomThemeExample.Json);
            Assert.Empty(errors);

            var palette = MobileSkinCatalog.FromCustomTheme(theme!);

            Assert.Equal("我的海边黄昏", palette.DisplayName);
            CustomThemeValidator.TryParseHexColor("#B32A1F40", out var expectedBackground);
            Assert.Equal(expectedBackground, palette.Background);
            CustomThemeValidator.TryParseHexColor("#F9C784", out var expectedAccent);
            Assert.Equal(expectedAccent, palette.Accent);
            CustomThemeValidator.TryParseHexColor("#FFF3E0", out var expectedText);
            Assert.Equal(expectedText, palette.Text);
        }

        [Fact]
        public void FromCustomTheme_MissingName_FallsBackToGenericDisplayName()
        {
            var theme = new CustomTheme
            {
                Colors = new CustomThemeColors { LyricBoxBg = "#000000", Accent = "#FFFFFF", Lyric = "#FFFFFF" },
            };

            var palette = MobileSkinCatalog.FromCustomTheme(theme);

            Assert.Equal("客制化主题", palette.DisplayName);
        }
    }
}
