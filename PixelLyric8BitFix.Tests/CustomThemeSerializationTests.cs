using System.Collections.Generic;
using Newtonsoft.Json;
using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    /// <summary>
    /// 钉住一个真实炸过的 bug：CustomThemeValidator.SerializerSettings（CustomThemeStore.Save 存盘时用
    /// 这份设置把 CustomTheme 序列化回 JSON）不能用默认的 CamelCasePropertyNamesContractResolver()——
    /// 它会把 Dictionary&lt;string,string&gt; 的 key 也一起转小写，而 icon.palette / layers[i].icon.palette
    /// 的 key 是画板/用户自己分配的单字符调色板符号，大小写是有意义的两个不同颜色（比如 PixelIconEditor.
    /// AssignableChars 里 'f' 和 'F' 就是分开分配的）。真实症状：存了一个用大写字母当调色板 key 的图标，
    /// 存的时候校验是过的（用的是内存里原始对象），但存盘这一步把 "A"/"a" 这种本该独立的两个 key 挤成
    /// 同一个、序列化出重复键、反序列化丢一个——下次刷新"已保存的客制化主题"列表重新读盘校验，rows 里
    /// 引用的那些大写符号在 palette 里已经找不到对应颜色了，这份主题直接从列表里静默消失，
    /// 看起来就像"点了保存但完全没反应"。</summary>
    public class CustomThemeSerializationTests
    {
        [Fact]
        public void SerializerSettings_PreservesDictionaryKeyCase()
        {
            var palette = new Dictionary<string, string>
            {
                ["A"] = "#111111",
                ["a"] = "#222222",
                ["B"] = "#333333",
                ["1"] = "#444444",
            };

            string json = JsonConvert.SerializeObject(palette, Formatting.Indented, CustomThemeValidator.SerializerSettings);
            var roundTripped = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);

            Assert.Equal(4, roundTripped!.Count);
            Assert.Equal("#111111", roundTripped["A"]);
            Assert.Equal("#222222", roundTripped["a"]);
        }

        [Fact]
        public void SerializerSettings_StillCamelCasesPropertyNames()
        {
            // 字典 key 不转是刻意的，但属性名（Name -> name、Colors -> colors……）还是要照样转 camelCase，
            // 不能因为修字典 key 那个坑就把整个 camelCase 功能关掉
            var theme = new CustomTheme { Name = "测试" };
            string json = JsonConvert.SerializeObject(theme, CustomThemeValidator.SerializerSettings);

            Assert.Contains("\"name\":\"测试\"", json);
            Assert.DoesNotContain("\"Name\":", json);
        }

        [Fact]
        public void SaveThenReparse_MixedCasePaletteIcon_RoundTripsAndStillValidates()
        {
            // 端到端版：拼一份用大小写混合调色板 key 的合法主题，走一遍"序列化成盘上会长的样子 -> 再解析
            // 校验一次"，模拟 CustomThemeStore.Save 存盘、CustomThemeStore.ListAll 下次读盘重新校验这两步，
            // 不直接碰真实磁盘（保持纯单元测试、不依赖 AppData）
            const string original = @"{
              ""name"": ""大调色板图标"",
              ""colors"": { ""title"": ""#FFFFFF"", ""artist"": ""#FFFFFF"", ""accent"": ""#FFFFFF"", ""lyric"": ""#FFFFFF"", ""lyricBoxBg"": ""#000000"", ""lyricBoxBorder"": ""#000000"" },
              ""background"": { ""type"": ""solid"", ""stops"": [""#000000""] },
              ""icon"": { ""palette"": { ""A"": ""#111111"", ""a"": ""#222222"", ""B"": ""#333333"" }, ""rows"": [""AaBA"", ""BAaB"", ""aBAa"", ""AaBA""] },
              ""animation"": { ""type"": ""pulse"" }
            }";

            var (theme, parseErrors) = CustomThemeValidator.ParseAndValidate(original);
            Assert.NotNull(theme);
            Assert.Empty(parseErrors);

            string savedJson = JsonConvert.SerializeObject(theme, Formatting.Indented, CustomThemeValidator.SerializerSettings);
            var (reloaded, reloadErrors) = CustomThemeValidator.ParseAndValidate(savedJson);

            Assert.True(reloadErrors.Count == 0, $"重新读盘校验失败：{string.Join(" | ", reloadErrors)}\nJSON:\n{savedJson}");
            Assert.NotNull(reloaded);
        }
    }
}
