using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 成就墙：8 张卡片（7 个常规成就 + 1 个压轴），全部从本地听歌统计现算出来（AchievementCalculator），
    /// 没有独立的"已解锁"存档——纯展示页，不涉及任何网络请求。
    /// </summary>
    public partial class AchievementsWindow : Window
    {
        public AchievementsWindow()
        {
            InitializeComponent();

            var stats = ListeningStatsStore.Load();
            var results = AchievementCalculator.Evaluate(stats);
            TxtProgress.Text = $"{results.Count(r => r.Unlocked)} / {results.Count} 已解锁";

            foreach (var progress in results)
            {
                AchievementPanel.Children.Add(BuildCard(progress));
            }
        }

        // 压轴那张（尊贵听众）额外标出"解锁「尊贵皇冠」限定皮肤"这行奖励文案，边框用金色跟其它
        // 7 张常规成就区分开；没解锁的卡片整体降低不透明度 + 叠一个 🔒，不用额外做灰度处理那么复杂，
        // 透明度已经足够说明"这张还没点亮"
        private static UIElement BuildCard(AchievementProgress progress)
        {
            bool isCapstone = progress.Achievement.Id == AchievementCalculator.CrownSkin.Id;
            var accentColor = isCapstone ? Color.FromRgb(0xE6, 0xB6, 0x55) : Color.FromRgb(0x34, 0xD3, 0x99);

            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = progress.Achievement.Icon,
                FontSize = 28,
                Margin = new Thickness(0, 0, 0, 6),
            });
            content.Children.Add(new TextBlock
            {
                Text = progress.Achievement.Name,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF5, 0xF1)),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
            });
            content.Children.Add(new TextBlock
            {
                Text = progress.Achievement.Description,
                Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xA2, 0x96)),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });
            if (isCapstone)
            {
                content.Children.Add(new TextBlock
                {
                    Text = "🎁 解锁「尊贵皇冠」限定皮肤",
                    Foreground = new SolidColorBrush(accentColor),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 8, 0, 0),
                });
            }

            UIElement cardChild = content;
            if (!progress.Unlocked)
            {
                var lockBadge = new Border
                {
                    Width = 26,
                    Height = 26,
                    CornerRadius = new CornerRadius(13),
                    Background = new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0)),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, -8, -8, 0),
                    Child = new TextBlock { Text = "🔒", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                };
                var wrapper = new Grid();
                wrapper.Children.Add(content);
                wrapper.Children.Add(lockBadge);
                cardChild = wrapper;
            }

            return new Border
            {
                Width = 200,
                MinHeight = 116,
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(14),
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x21, 0x1C)),
                BorderBrush = new SolidColorBrush(progress.Unlocked ? accentColor : Color.FromRgb(0x2A, 0x33, 0x2B)),
                BorderThickness = new Thickness(isCapstone ? 2 : 1),
                Opacity = progress.Unlocked ? 1.0 : 0.55,
                Child = cardChild,
            };
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e) => Close();
    }
}
