using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 把 ListeningSummary 现场拼成一张固定尺寸的分享卡片（WPF 视觉树），再用 RenderTargetBitmap
    /// 转成图片——不依赖任何外部图片资源。卡片背景固定用暗色（保证不管选的是哪套主题的强调色，
    /// 文字对比度都够看），但强调色（标题/分隔线/小节标题/边框）跟着调用方传进来的 SkinTheme 走，
    /// 右上角还会贴一个那套主题自己的图标当"吉祥物"——具体选哪套主题由 ListeningStatsWindow 那边
    /// 的"卡片风格"下拉框决定，不一定是用户当前正在用的皮肤。渲染/存图逻辑（RenderTargetBitmap/
    /// PngBitmapEncoder/存文件）留给 ListeningStatsWindow 处理，这里只管"拼视觉树"。
    /// </summary>
    internal static class ShareCardBuilder
    {
        public const int CardWidth = 640;
        public const int CardHeight = 960;

        private static readonly Color BgColor = Color.FromRgb(0x0D, 0x0F, 0x14);
        private static readonly Color SubTextColor = Color.FromRgb(0x88, 0x88, 0x88);
        private static readonly FontFamily CardFont = new("Consolas");

        public static FrameworkElement Build(ListeningSummary summary, string periodLabel, string dateRangeLabel, SkinTheme theme, string? userName = null)
        {
            var accentBrush = new SolidColorBrush(theme.Accent);

            var content = new StackPanel { Margin = new Thickness(36, 36, 100, 96) }; // 右边多留一截（100 而不是 36），不跟右上角的吉祥物图标叠在一起；底部留够空间给贴底的 footer

            content.Children.Add(new TextBlock
            {
                Text = "🎵 ZIPPLAY 听歌报告",
                Foreground = accentBrush,
                FontFamily = CardFont,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
            });
            // 有名字就加一行"XXX 的"，没填就跟以前一样只显示周期——留空不留怪异的空白行
            string subtitleText = string.IsNullOrEmpty(dateRangeLabel) ? periodLabel : $"{periodLabel} · {dateRangeLabel}";
            if (!string.IsNullOrWhiteSpace(userName))
            {
                subtitleText = $"{userName} 的 · {subtitleText}";
            }
            content.Children.Add(new TextBlock
            {
                Text = subtitleText,
                Foreground = new SolidColorBrush(SubTextColor),
                FontFamily = CardFont,
                FontSize = 13,
                Margin = new Thickness(0, 4, 0, 0),
            });

            content.Children.Add(Divider(theme.Accent));
            content.Children.Add(BigStat(ListeningStatsAggregator.FormatDuration(summary.TotalSeconds), "总时长"));
            content.Children.Add(BigStat($"{summary.ActiveDayCount} 天", "有听歌"));

            content.Children.Add(Divider(theme.Accent));
            content.Children.Add(SectionLabel("🎤 最常听的艺人", accentBrush));
            content.Children.Add(RankList(summary.TopArtists.Select(a => (a.Artist, a.Seconds))));

            content.Children.Add(Divider(theme.Accent));
            content.Children.Add(SectionLabel("🎵 最常听的歌曲", accentBrush));
            content.Children.Add(RankList(summary.TopTracks.Select(t =>
                (string.IsNullOrEmpty(t.Artist) ? t.Title : $"{t.Title} · {t.Artist}", t.Seconds))));

            var footer = new TextBlock
            {
                Text = "ZipPlay · 像素歌词播放器",
                Foreground = new SolidColorBrush(SubTextColor),
                FontFamily = CardFont,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 30),
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

        // 右上角的"吉祥物"：直接用这套主题在 Mini 模式小方块上用的同一个图标（SkinTheme.MiniIcon），
        // 连底板配色都复用 MiniBg/MiniBorder——这本来就是"代表这套主题的一个小图标"该有的待遇，
        // 不用为了卡片专门再画一套
        private static UIElement MascotIcon(SkinTheme theme)
        {
            var image = new Image
            {
                Source = theme.MiniIcon(),
                Width = 56,
                Height = 56,
                Stretch = Stretch.Uniform,
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor); // 保持像素图标的锐利边缘，不被拉伸模糊

            var badge = new Border
            {
                Width = 76,
                Height = 76,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(theme.MiniBg),
                BorderBrush = new SolidColorBrush(theme.MiniBorder),
                BorderThickness = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 32, 32, 0),
                Child = image,
            };
            return badge;
        }

        private static UIElement Divider(Color accent) => new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromArgb(0x40, accent.R, accent.G, accent.B)),
            Margin = new Thickness(0, 18, 0, 18),
        };

        private static UIElement BigStat(string value, string label)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            row.Children.Add(new TextBlock
            {
                Text = value,
                Foreground = Brushes.White,
                FontFamily = CardFont,
                FontSize = 30,
                FontWeight = FontWeights.Bold,
            });
            row.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(SubTextColor),
                FontFamily = CardFont,
                FontSize = 14,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
                Padding = new Thickness(0, 0, 0, 6),
            });
            return row;
        }

        private static UIElement SectionLabel(string text, Brush accentBrush) => new TextBlock
        {
            Text = text,
            Foreground = accentBrush,
            FontFamily = CardFont,
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 10),
        };

        // 榜单没数据（比如这段时间只听了一首歌，Top5 艺人只有一个）就只画有的那几行，不用补空行凑数
        private static UIElement RankList(IEnumerable<(string Name, int Seconds)> items)
        {
            var panel = new StackPanel();
            int i = 1;
            foreach (var (name, seconds) in items)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var nameText = new TextBlock
                {
                    Text = $"{i}. {name}",
                    Foreground = Brushes.White,
                    FontFamily = CardFont,
                    FontSize = 15,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                Grid.SetColumn(nameText, 0);

                var secondsText = new TextBlock
                {
                    Text = ListeningStatsAggregator.FormatDuration(seconds),
                    Foreground = new SolidColorBrush(SubTextColor),
                    FontFamily = CardFont,
                    FontSize = 13,
                    Margin = new Thickness(10, 0, 0, 0),
                };
                Grid.SetColumn(secondsText, 1);

                row.Children.Add(nameText);
                row.Children.Add(secondsText);
                panel.Children.Add(row);
                i++;
            }
            if (i == 1) // 一行都没画出来
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "暂无数据",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                    FontFamily = CardFont,
                    FontSize = 13,
                });
            }
            return panel;
        }

        /// <summary>把拼好的视觉树渲染成一张位图——不依赖它在不在窗口的可视化树里，
        /// 手动 Measure/Arrange 一遍就能渲染，这是 WPF 离屏截图的标准写法。</summary>
        public static RenderTargetBitmap RenderToBitmap(FrameworkElement visual)
        {
            visual.Measure(new Size(CardWidth, CardHeight));
            visual.Arrange(new Rect(0, 0, CardWidth, CardHeight));
            visual.UpdateLayout();

            var bitmap = new RenderTargetBitmap(CardWidth, CardHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            return bitmap;
        }
    }
}
