using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 首页：便当格布局，每个格子对应一个独立的小设置页，点进去才看到具体内容——取代以前那个
    /// 从头滚到尾的 SettingsWindow。两个大格子（皮肤主题、听歌统计）带一行实时小字（当前皮肤名字/
    /// 累计听了多久），其余格子只是入口，不需要预览信息。左上角头像 + 问候语一起是"改个人资料"
    /// （名字/头像/导出导入）的入口，不单独占一个格子。
    /// </summary>
    public partial class HomeWindow : Window
    {
        // App 用的是 ShutdownMode="OnExplicitShutdown"：这个窗口有可能是 Splash 之后（或者 Welcome
        // 首次填完名字之后）直接顶上来的"当前唯一窗口"，关掉它（不管是点"开始播放"正常进 MainWindow，
        // 还是直接按标题栏的 X）都要自己决定是继续流程还是退出整个 App
        private readonly WindowProceedGuard _nav;

        public HomeWindow()
        {
            InitializeComponent();

            _nav = new WindowProceedGuard(this);

            var settings = AppSettings.Load();
            ChkSkipStartupSettings.IsChecked = settings.SkipStartupSettings;

            RefreshGreeting();
            RefreshAvatarDisplay();
            BuildTiles();
        }

        private void RefreshGreeting()
        {
            var settings = AppSettings.Load();
            TxtGreeting.Text = string.IsNullOrWhiteSpace(settings.UserName)
                ? "设置你的名字/头像 →"
                : $"你好，{settings.UserName} 👋（点这里改资料）";
        }

        private void RefreshAvatarDisplay()
        {
            var avatar = AvatarStore.Load();
            AvatarBrush.ImageSource = avatar;
            TxtAvatarPlaceholder.Visibility = avatar == null ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Profile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            new WelcomeWindow(isFirstRun: false) { Owner = this }.ShowDialog();
            RefreshGreeting();
            RefreshAvatarDisplay();
        }

        // 两个大格子要显示的实时小字：当前皮肤名字、累计听了多久。都是现读现算，不缓存——
        // 格子本来就不多，重新读一次 AppSettings/ListeningStats 的开销可以忽略不计
        private void BuildTiles()
        {
            LargeTilePanel.Children.Clear();
            SmallTilePanel.Children.Clear();

            var settings = AppSettings.Load();
            var stats = ListeningStatsStore.Load();
            int totalSeconds = ListeningStatsAggregator.GetTotalSeconds(stats, DateOnly.MinValue, DateOnly.MaxValue);
            string statsSubtitle = totalSeconds > 0 ? $"共听了 {ListeningStatsAggregator.FormatDuration(totalSeconds)}" : "还没有听歌记录";

            LargeTilePanel.Children.Add(BuildTile("🎨", "皮肤主题", GetCurrentSkinLabel(settings), height: 150, OpenDialog<SkinPickerWindow>));
            LargeTilePanel.Children.Add(BuildTile("📊", "听歌统计", statsSubtitle, height: 150, OpenDialog<ListeningStatsWindow>));

            SmallTilePanel.Children.Add(BuildTile("🖌️", "自定义主题", null, height: 120, OpenDialog<CustomThemeWindow>));
            SmallTilePanel.Children.Add(BuildTile("🏆", "成就墙", null, height: 120, OpenDialog<AchievementsWindow>));
            SmallTilePanel.Children.Add(BuildTile("📐", "窗口与显示", null, height: 120, OpenDialog<PlayerAppearanceWindow>));
            SmallTilePanel.Children.Add(BuildTile("🎵", "音频律动", null, height: 120, OpenDialog<AudioReactiveSettingsWindow>));
            SmallTilePanel.Children.Add(BuildTile("ℹ️", "关于与更新", null, height: 120, OpenDialog<AboutWindow>));
            SmallTilePanel.Children.Add(BuildTile("🗄️", "本地数据", null, height: 120, OpenDialog<LocalDataWindow>));
        }

        private static string GetCurrentSkinLabel(AppSettings settings)
        {
            if (settings.Skin == PlayerSkin.Custom)
            {
                var theme = !string.IsNullOrEmpty(settings.CustomThemeFile) ? CustomThemeStore.Load(settings.CustomThemeFile) : null;
                return theme != null ? $"🎨 {theme.Name}" : "🎨 自定义主题";
            }
            return SkinTheme.GetDisplayName(settings.Skin);
        }

        // 每个格子点开都是同一套动作：开那个小窗口（ShowDialog，首页在背后保持不动）→ 关掉之后
        // 重新读一遍数据刷新格子——不管点的是哪个格子都这么处理，不用给每个格子单独写一份"点完要不要刷新"的判断
        private void OpenDialog<T>() where T : Window, new()
        {
            new T { Owner = this }.ShowDialog();
            RefreshGreeting();
            BuildTiles();
        }

        private Button BuildTile(string icon, string title, string? subtitle, double height, Action onClick)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = icon, FontSize = 26, Margin = new Thickness(0, 0, 0, 8) });
            stack.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF5, 0xF1)),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
            });
            if (!string.IsNullOrEmpty(subtitle))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = subtitle,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xA2, 0x96)),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                });
            }

            var button = new Button
            {
                Style = (Style)FindResource("TileButtonStyle"),
                Height = height,
                Content = stack,
            };
            button.Click += (s, e) => onClick();
            return button;
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            var settings = AppSettings.Load();
            settings.SkipStartupSettings = ChkSkipStartupSettings.IsChecked == true;
            settings.Save();

            _nav.ProceedTo(new MainWindow(settings));
        }
    }
}
