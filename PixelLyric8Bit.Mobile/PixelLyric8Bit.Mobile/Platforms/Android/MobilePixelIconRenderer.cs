using Android.Graphics;

namespace PixelLyric8Bit.Mobile.Droid;

/// <summary>
/// 把一份像素图标（字符网格 + RgbaColor 调色板——跟桌面版 PixelArt.Build 吃的是同一个形状，见
/// PixelLyric8Bit.Core 的 MobileSkinIcon）画成一张 Android.Graphics.Bitmap，给悬浮窗的 ImageView 用。
///
/// 跟 Uno 那边的 PixelIconRenderer.cs 是同一个数据源、不同的渲染代码（两边 Bitmap 类型来自不同图形栈，
/// 没法共享渲染这一步，能共享的是数据）——这也是当初把 CustomTheme/PixelIconEditor 搬去 Core 时特意
/// 排除渲染这一步不搬的原因，见 CustomThemeColorInterop.cs 的说明，Android 这边现在补上自己的版本。
///
/// scale 手动逐像素放大（每个源像素变成 scale×scale 的一块实心色块），不靠 ImageView 自己拉伸——
/// 那样默认会走双线性插值把硬边缘磨糊，像素画放大最讲究的就是保留硬边缘，道理跟桌面版
/// IconPainterWindow.xaml.cs 的 ScaleNearestNeighbor 一样。
/// </summary>
internal static class MobilePixelIconRenderer
{
    public static Bitmap Render(PixelLyric8BitFix.MobileSkinIcon icon, int scale = 6)
    {
        var rows = icon.Rows;
        int width = rows[0].Length;
        int height = rows.Length;
        int outW = width * scale, outH = height * scale;

        var bitmap = Bitmap.CreateBitmap(outW, outH, Bitmap.Config.Argb8888!)!;

        for (int y = 0; y < height; y++)
        {
            string row = rows[y];
            for (int x = 0; x < width; x++)
            {
                char c = row[x];
                var rgba = icon.Palette.TryGetValue(c, out var color) ? color : PixelLyric8BitFix.RgbaColor.Transparent;
                var androidColor = Color.Argb(rgba.A, rgba.R, rgba.G, rgba.B);

                int baseX = x * scale, baseY = y * scale;
                for (int dy = 0; dy < scale; dy++)
                {
                    for (int dx = 0; dx < scale; dx++)
                    {
                        bitmap.SetPixel(baseX + dx, baseY + dy, androidColor);
                    }
                }
            }
        }

        return bitmap;
    }

    /// <summary>逐帧动画版本——客制化主题的 icon.frames（每帧一份字符网格，调色板是所有帧共用的一份，
    /// 跟桌面版 CustomThemeIcon.Frames 是同一个形状，见 CustomTheme.cs 的注释）。挨个调用上面的
    /// Render 画出每一帧，画完之后怎么循环播放（多久切一帧、切的时候要不要跟着音乐变速）不归这里管——
    /// 这里只管"数据转位图"，调用方（FloatingOverlayService 的 IconFrameTick）自己拿计时器切换，
    /// 跟桌面版 CustomThemeColorInterop.BuildCustomIconFrames 的分工是同一个道理。</summary>
    public static Bitmap[] RenderFrames(IReadOnlyList<string[]> frames, IReadOnlyDictionary<char, PixelLyric8BitFix.RgbaColor> palette, int scale = 6)
    {
        var result = new Bitmap[frames.Count];
        for (int i = 0; i < frames.Count; i++)
        {
            result[i] = Render(new PixelLyric8BitFix.MobileSkinIcon(frames[i], palette), scale);
        }
        return result;
    }
}
