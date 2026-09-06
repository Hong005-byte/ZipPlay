using System.Collections.Generic;
using System.Text;
using Android.Graphics;
using PixelLyric8BitFix;
using Canvas = Android.Graphics.Canvas; // 跟 Uno 的 Microsoft.UI.Xaml.Controls.Canvas 撞名，这个文件只画 Android 位图，全部按 Android 那个来

namespace PixelLyric8Bit.Mobile.Droid;

/// <summary>
/// 🖼️ 歌词分享卡片——把悬浮窗正在显示的这句歌词画成一张能分享出去的图片，布局照抄桌面版
/// LyricShareCardBuilder（❝ 引号 + 歌词 + —— 歌名·艺人 + 左上角吉祥物图标 + 底部落款），只是这边
/// 直接用 Android.Graphics.Canvas 画位图，不是拼一棵 WPF 视觉树再离屏渲染——那一套（UIElement ->
/// RenderTargetBitmap）在这边没有对应物，两边"画出来长什么样"一样，"怎么画出来"是完全独立的两份实现。
///
/// 故意没用 StaticLayout/TextPaint 这套 Android 文本排版 API 来处理歌词自动换行，自己按字符宽度
/// 手动截行（见 WrapText）——中文歌词大多没有空格，StaticLayout 默认按空格/标点断行的策略对纯中文
/// 长句效果不一定好，逐字符量宽度、装不下就换行，虽然糙但对"一句歌词"这种短文本已经够用，
/// 也避免了再引入一套没用过的排版 API。
/// </summary>
internal static class LyricShareCardRenderer
{
    // 桌面版 640x400（8:5），这边等比放大到 960x600——分享出去的图经常会被压缩/在别人的小屏幕上看，
    // 分辨率高一点更保险
    public const int CardWidth = 960;
    public const int CardHeight = 600;

    public static Bitmap Render(string lyricLine, string title, string artist, MobileSkinPalette palette, Bitmap? icon)
    {
        var bitmap = Bitmap.CreateBitmap(CardWidth, CardHeight, Bitmap.Config.Argb8888!)!;
        var canvas = new Canvas(bitmap);
        var accent = ToAndroidColor(palette.Accent);

        var bgPaint = new Paint { AntiAlias = true, Color = Color.Rgb(0x0D, 0x0F, 0x14) };
        canvas.DrawRoundRect(new RectF(0, 0, CardWidth, CardHeight), 36, 36, bgPaint);

        var borderPaint = new Paint { AntiAlias = true, Color = accent, StrokeWidth = 8 };
        borderPaint.SetStyle(Paint.Style.Stroke);
        canvas.DrawRoundRect(new RectF(4, 4, CardWidth - 4, CardHeight - 4), 36, 36, borderPaint);

        // 左上角吉祥物图标——没有（比如皮肤/主题压根没配得出图标）就跳过，不留一块空占位框
        const float iconMargin = 36, iconCardSize = 96;
        if (icon != null)
        {
            var iconBgPaint = new Paint { AntiAlias = true, Color = Color.Rgb(0x1A, 0x1A, 0x1A) };
            var iconRect = new RectF(iconMargin, iconMargin, iconMargin + iconCardSize, iconMargin + iconCardSize);
            canvas.DrawRoundRect(iconRect, 18, 18, iconBgPaint);

            var iconBorderPaint = new Paint { AntiAlias = true, Color = accent, StrokeWidth = 3 };
            iconBorderPaint.SetStyle(Paint.Style.Stroke);
            canvas.DrawRoundRect(iconRect, 18, 18, iconBorderPaint);

            float pad = 12;
            var dest = new RectF(iconRect.Left + pad, iconRect.Top + pad, iconRect.Right - pad, iconRect.Bottom - pad);
            canvas.DrawBitmap(icon, null, dest, null);
        }

        float contentLeft = 72, contentRight = CardWidth - 72;
        float y = 200;

        var quotePaint = new Paint { AntiAlias = true, Color = accent, TextSize = 64 };
        quotePaint.SetTypeface(Typeface.Create("monospace", TypefaceStyle.Bold));
        canvas.DrawText("❝", contentLeft, y, quotePaint); // ❝

        y += 30;
        var lyricPaint = new Paint { AntiAlias = true, Color = Color.White, TextSize = 40 };
        lyricPaint.SetTypeface(Typeface.Create("monospace", TypefaceStyle.Bold));
        float maxLineWidth = contentRight - contentLeft;
        foreach (string line in WrapText(lyricLine, lyricPaint, maxLineWidth))
        {
            canvas.DrawText(line, contentLeft, y, lyricPaint);
            y += lyricPaint.FontSpacing; // 用字体自带的推荐行距，中英文混排也不会挤在一起
        }

        y += 34;
        string byline = string.IsNullOrWhiteSpace(artist) ? title : $"{title} · {artist}";
        var bylinePaint = new Paint { AntiAlias = true, Color = Color.Rgb(0x88, 0x88, 0x88), TextSize = 24 };
        canvas.DrawText($"—— {byline}", contentLeft, y, bylinePaint);

        var footerPaint = new Paint { AntiAlias = true, Color = Color.Rgb(0x88, 0x88, 0x88), TextSize = 20 };
        footerPaint.TextAlign = Paint.Align.Center;
        canvas.DrawText("ZipPlay · 像素歌词播放器", CardWidth / 2f, CardHeight - 36, footerPaint);

        return bitmap;
    }

    // 逐字符量宽度、装不下就换行——见类顶部注释为什么不用 StaticLayout。理论上一个 Unicode 代理对
    // （emoji 之类）会被这个 char 级别的循环从中间切开，但歌词文本本来就极少见到 emoji，真出现了
    // 顶多是这一张分享图上这一个字符显示不对，不影响别的
    private static List<string> WrapText(string text, Paint paint, float maxWidth)
    {
        var lines = new List<string>();
        var current = new StringBuilder();
        foreach (char c in text)
        {
            current.Append(c);
            if (paint.MeasureText(current.ToString()) > maxWidth && current.Length > 1)
            {
                current.Length -= 1; // 这个字放不下了，退回去，留到下一行开头
                lines.Add(current.ToString());
                current.Clear();
                current.Append(c);
            }
        }
        if (current.Length > 0) lines.Add(current.ToString());
        return lines.Count > 0 ? lines : new List<string> { text };
    }

    private static Color ToAndroidColor(RgbaColor c) => Color.Argb(c.A, c.R, c.G, c.B);
}
