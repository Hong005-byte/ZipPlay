using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using PixelLyric8BitFix;

namespace PixelLyric8Bit.Mobile;

/// <summary>
/// 把一份像素网格（字符行 + RgbaColor 调色板——跟桌面版 PixelArt.cs/PixelIconEditor 用的是同一套
/// "字符网格 + 调色板"数据形状，颜色类型也是同一个 PixelLyric8Bit.Core 里的 RgbaColor）画成一张
/// WriteableBitmap，给 Uno 这边的 Image 控件用。
///
/// 没有直接复用桌面版 PixelArt.cs——那边画的是 System.Windows.Media.Imaging.WriteableBitmap（WPF
/// 专属类型），这边是 Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap（WinUI/Uno 的），两个类型名字
/// 一样但来自不同的图形栈，没法共享同一份渲染代码；能共享的是"数据"这一层（字符网格、RgbaColor
/// 调色板），渲染这一步各平台各写各的，这也是当初把 CustomTheme/PixelIconEditor 搬去 Core 时特意
/// 排除 BuildCustomIconFrames（渲染）不搬的原因，见 CustomThemeColorInterop.cs 的说明。
///
/// scale 手动逐像素放大（每个源像素变成 scale×scale 的一块实心色块），不靠 Image 控件自己拉伸——
/// 那样默认会走双线性插值把硬边缘磨糊，像素画放大最讲究的就是保留硬边缘，这点跟桌面版
/// IconPainterWindow.xaml.cs 的 ScaleNearestNeighbor 是同一个道理。
/// </summary>
internal static class PixelIconRenderer
{
    public static WriteableBitmap Render(string[] rows, IReadOnlyDictionary<char, RgbaColor> palette, int scale = 6)
    {
        int width = rows[0].Length;
        int height = rows.Length;
        int outW = width * scale, outH = height * scale;

        var bitmap = new WriteableBitmap(outW, outH);
        var pixels = new byte[outW * outH * 4]; // BGRA8，WriteableBitmap.PixelBuffer 的像素格式

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                char c = rows[y][x];
                byte a = 0, r = 0, g = 0, b = 0;
                if (c != '.' && palette.TryGetValue(c, out var color))
                {
                    a = color.A; r = color.R; g = color.G; b = color.B;
                }

                for (int dy = 0; dy < scale; dy++)
                {
                    int outY = y * scale + dy;
                    int rowStart = (outY * outW + x * scale) * 4;
                    for (int dx = 0; dx < scale; dx++)
                    {
                        int idx = rowStart + dx * 4;
                        pixels[idx + 0] = b;
                        pixels[idx + 1] = g;
                        pixels[idx + 2] = r;
                        pixels[idx + 3] = a;
                    }
                }
            }
        }

        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(pixels, 0, pixels.Length);
        }
        bitmap.Invalidate();
        return bitmap;
    }

    /// <summary>逐帧动画版本——CustomThemePage 的实时预览要看 icon.frames 的动画效果，跟 Android 端
    /// FloatingOverlayService 用的 MobilePixelIconRenderer.RenderFrames 是同一个思路，只是这边画出来的
    /// 是 Uno 的 WriteableBitmap 给 Image 控件用，不是 Android.Graphics.Bitmap 给原生 ImageView 用——
    /// 两边图形栈不同，没法共享这一步，能共享的是数据（见类顶部注释）。循环播放归调用方
    /// （CustomThemePage 的 PreviewFrameTick）自己管，这里只管"数据转位图"。</summary>
    public static WriteableBitmap[] RenderFrames(IReadOnlyList<string[]> frames, IReadOnlyDictionary<char, RgbaColor> palette, int scale = 6)
    {
        var result = new WriteableBitmap[frames.Count];
        for (int i = 0; i < frames.Count; i++)
        {
            result[i] = Render(frames[i], palette, scale);
        }
        return result;
    }
}
