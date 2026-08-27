using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// CustomThemeValidator（现在在 PixelLyric8Bit.Core，颜色用平台无关的 RgbaColor，方便以后 Android/Uno
    /// 版本复用同一份校验/解析逻辑）跟这边 WPF 渲染之间的桥——WPF 这边要 System.Windows.Media.Color、
    /// 要真的画出一张 BitmapSource 的地方，都走这个类转一层，不直接碰 Core 里的 RgbaColor。
    ///
    /// BuildCustomIconFrames 故意没有留在 CustomThemeValidator 里——那是"把数据画成位图"，是 UI 渲染
    /// 的活，不是数据转换/校验，各平台的图形 API 不一样（这边是 BitmapSource，Android 会是完全不同的
    /// 东西），搬去 Core 反而会把渲染细节泄漏进本该纯数据的那一层。
    /// </summary>
    internal static class CustomThemeColorInterop
    {
        public static bool TryParseHexColor(string hex, out Color color)
        {
            bool ok = CustomThemeValidator.TryParseHexColor(hex, out var c);
            color = Color.FromArgb(c.A, c.R, c.G, c.B);
            return ok;
        }

        private static Dictionary<char, Color> BuildIconPalette(CustomThemeIcon icon) =>
            CustomThemeValidator.BuildIconPalette(icon)
                .ToDictionary(kv => kv.Key, kv => Color.FromArgb(kv.Value.A, kv.Value.R, kv.Value.G, kv.Value.B));

        /// <summary>把一份 icon 渲染成一串位图——没有 Frames 就是长度为 1 的数组（跟这个字段加进来之前
        /// "只有一张静态图"完全一样的行为），有 Frames 就按顺序逐帧渲染。调用方（正式渲染 MainWindow.
        /// Skins.cs、编辑页预览 CustomThemeWindow.xaml.cs）拿到这个数组之后自己决定要不要做逐帧切换——
        /// 这里只管"数据转位图"，不碰 UI/计时器。调用前应该已经过 ValidateIcon 校验，这里不重复校验。</summary>
        public static BitmapSource[] BuildCustomIconFrames(CustomThemeIcon icon)
        {
            var palette = BuildIconPalette(icon);
            if (icon.Frames is { Count: > 0 } frames)
            {
                return frames.Select(f => PixelArt.BuildCustomIcon(f.ToArray(), palette)).ToArray();
            }
            return new[] { PixelArt.BuildCustomIcon(icon.Rows!.ToArray(), palette) };
        }
    }
}
