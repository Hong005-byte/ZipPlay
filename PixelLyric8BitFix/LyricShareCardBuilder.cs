using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// "🖼️ 生成当前歌词分享卡片"背后的拼视觉树逻辑——跟 ShareCardBuilder（听歌统计总结卡片）是
    /// 同一个套路（固定尺寸的 WPF 视觉树 -> 调用方用 ShareCardBuilder.RenderToBitmap 离屏渲染成图片），
    /// 内容完全不一样：这里只有一句正在显示的歌词 + 歌名/艺人，不是一整段时间的统计总结，尺寸也
    /// 窄了不少（960 那么高整段大部分是空的），所以没有共用 ShareCardBuilder.CardWidth/CardHeight，
    /// 但复用了它通用化之后的 RenderToBitmap(visual, width, height) 重载，不重新实现一遍离屏渲染。
    /// </summary>
    internal static class LyricShareCardBuilder
    {
        public const int CardWidth = 640;
        public const int CardHeight = 400;

        private static readonly Color BgColor = Color.FromRgb(0x0D, 0x0F, 0x14);
        private static readonly Color SubTextColor = Color.FromRgb(0x88, 0x88, 0x88);
        private static readonly FontFamily CardFont = new("Consolas");

        public static FrameworkElement Build(string lyricLine, string title, string artist, SkinTheme theme)
        {
            var accentBrush = new SolidColorBrush(theme.Accent);

            var content = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(48, 0, 48, 0),
            };

            // 引号用 accent 色、字号拉大，一眼看出"这是一句被摘出来的话"，不是整段歌词都长这样
            content.Children.Add(new TextBlock
            {
                Text = "❝",
                Foreground = accentBrush,
                FontFamily = CardFont,
                FontSize = 40,
                FontWeight = FontWeights.Bold,
            });
            content.Children.Add(new TextBlock
            {
                Text = lyricLine,
                Foreground = Brushes.White,
                FontFamily = CardFont,
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });

            string byline = string.IsNullOrWhiteSpace(artist) ? title : $"{title} · {artist}";
            content.Children.Add(new TextBlock
            {
                Text = $"—— {byline}",
                Foreground = new SolidColorBrush(SubTextColor),
                FontFamily = CardFont,
                FontSize = 14,
                Margin = new Thickness(0, 20, 0, 0),
            });

            var footer = new TextBlock
            {
                Text = "ZipPlay · 像素歌词播放器",
                Foreground = new SolidColorBrush(SubTextColor),
                FontFamily = CardFont,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 20),
            };

            var layers = new Grid { Width = CardWidth, Height = CardHeight };
            layers.Children.Add(content);
            layers.Children.Add(footer);
            layers.Children.Add(MascotIcon(theme));

            return new Border
            {
                Width = CardWidth,
                Height = CardHeight,
                Background = new SolidColorBrush(BgColor),
                BorderBrush = accentBrush,
                BorderThickness = new Thickness(3),
                Child = layers,
            };
        }

        // 跟 ShareCardBuilder.MascotIcon 完全同一份逻辑（同一套主题吉祥物图标），复制过来是因为那边是
        // 私有方法、这边窗口尺寸也不一样（贴左上角而不是右上角，横向内容不多留右边给它） ——
        // 抽公共方法收益不大，这几行本身很稳定
        private static UIElement MascotIcon(SkinTheme theme)
        {
            var image = new Image
            {
                Source = theme.MiniIcon(),
                Width = 44,
                Height = 44,
                Stretch = Stretch.Uniform,
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);

            return new Border
            {
                Width = 60,
                Height = 60,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(theme.MiniBg),
                BorderBrush = new SolidColorBrush(theme.MiniBorder),
                BorderThickness = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(24, 24, 0, 0),
                Child = image,
            };
        }
    }
}
