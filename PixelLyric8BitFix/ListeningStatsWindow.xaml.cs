using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 听歌统计报告页：本月/今年/全部时间三个范围切换，展示总时长、活跃天数、最常听的艺人/歌曲，
    /// 还能把当前这个范围的总结导出成一张分享卡片图片。数据来源 ListeningStatsStore.Load() 一次性
    /// 读进来，纯本地展示，不涉及任何网络请求。
    /// </summary>
    public partial class ListeningStatsWindow : Window
    {
        private readonly ListeningStats _stats;

        // RefreshSummary 里顺手存一份，BtnSaveCard_Click 直接用，不用重新算一遍
        private ListeningSummary? _currentSummary;
        private string _currentPeriodLabel = "";
        private string _currentDateRangeLabel = "";

        // "卡片风格"下拉框的每一项：内置皮肤只要存枚举值就够了；客制化主题额外存一份 CustomTheme 数据本身
        // （渲染卡片要用它的配色/图标）+ 文件名（只用来匹配"当前正在用的是不是这一份"，选默认项用）
        private sealed class CardStyleOption
        {
            public required string DisplayName { get; init; }
            public required PlayerSkin Skin { get; init; }
            public CustomTheme? CustomTheme { get; init; }
            public string? CustomThemeFile { get; init; }
            public override string ToString() => DisplayName; // ComboBox 默认就是拿 ToString() 当显示文字
        }

        public ListeningStatsWindow()
        {
            InitializeComponent();
            _stats = ListeningStatsStore.Load();
            PopulateCardStyleOptions();
            RefreshSummary();
        }

        // 20 套免费内置皮肤 + 已存的客制化主题（最多 5 个）都能选，默认选中用户当前播放器实际在用的那一套，
        // 图省事不用每次都自己再挑一遍。限定皮肤（目前只有 Crown）没解锁之前不出现在这个列表里——
        // 之前的想法是"卡片风格跟能不能玩这套皮肤是两件事"，但限定皮肤本来就是要靠成就墙挣的，
        // 挣都没挣到就能拿它的配色/图标出去生成分享卡片，等于变相白嫖了外观，跟"限定"这个定位矛盾，
        // 所以这里改成一起锁：没解锁 Crown 就不让它出现在卡片风格的候选项里
        private void PopulateCardStyleOptions()
        {
            bool crownUnlocked = AchievementCalculator.IsCrownSkinUnlocked(_stats);

            var options = new List<CardStyleOption>();
            foreach (PlayerSkin skin in Enum.GetValues<PlayerSkin>())
            {
                if (skin == PlayerSkin.Custom) continue; // 客制化主题下面单独按每一份具体的主题列出来，不用这个空壳枚举值
                if (skin == PlayerSkin.Crown && !crownUnlocked) continue;
                options.Add(new CardStyleOption { DisplayName = SkinTheme.GetDisplayName(skin), Skin = skin });
            }
            foreach (var entry in CustomThemeStore.ListAll())
            {
                options.Add(new CardStyleOption
                {
                    DisplayName = $"🎨 {entry.Theme.Name}",
                    Skin = PlayerSkin.Custom,
                    CustomTheme = entry.Theme,
                    CustomThemeFile = entry.FileName,
                });
            }
            CmbCardStyle.ItemsSource = options;

            var currentSettings = AppSettings.Load();
            var defaultOption = currentSettings.Skin == PlayerSkin.Custom
                ? options.FirstOrDefault(o => o.CustomThemeFile == currentSettings.CustomThemeFile)
                : options.FirstOrDefault(o => o.Skin == currentSettings.Skin);
            CmbCardStyle.SelectedItem = defaultOption ?? options.FirstOrDefault();
        }

        private SkinTheme ResolveSelectedCardTheme()
        {
            if (CmbCardStyle.SelectedItem is CardStyleOption { CustomTheme: not null } option)
            {
                return MainWindow.BuildSkinThemeFromCustom(option.CustomTheme);
            }
            if (CmbCardStyle.SelectedItem is CardStyleOption builtIn)
            {
                return SkinTheme.For(builtIn.Skin);
            }
            return SkinTheme.For(PlayerSkin.Simple); // 兜底：理论上不会走到这（PopulateCardStyleOptions 至少填了 20 套内置皮肤）
        }

        // XAML 里 RbPeriodMonth 默认 IsChecked="True"，会在 InitializeComponent 期间就触发这个事件——
        // 这时候 _stats 还没赋值（构造函数还没走到那一行），用 null 兜底跳过，构造函数末尾自己会再调一次
        private void Period_Checked(object sender, RoutedEventArgs e)
        {
            if (_stats == null) return;
            RefreshSummary();
        }

        private (DateOnly From, DateOnly To) GetSelectedRange()
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            if (RbPeriodMonth.IsChecked == true)
            {
                return (new DateOnly(today.Year, today.Month, 1), today);
            }
            if (RbPeriodYear.IsChecked == true)
            {
                return (new DateOnly(today.Year, 1, 1), today);
            }
            return (DateOnly.MinValue, DateOnly.MaxValue); // 全部时间：不设下限/上限，交给聚合逻辑按实际有记录的天数算
        }

        // 分享卡片标题那行用的文案，跟着当前选的范围变——"全部时间"没有具体的起止日期，不用硬凑一个
        private (string PeriodLabel, string DateRangeLabel) GetPeriodLabels()
        {
            var today = DateTime.Now;
            if (RbPeriodMonth.IsChecked == true) return ("本月", $"{today.Year}年{today.Month}月");
            if (RbPeriodYear.IsChecked == true) return ("今年", $"{today.Year}年");
            return ("全部时间", "");
        }

        private void RefreshSummary()
        {
            var (from, to) = GetSelectedRange();
            var summary = ListeningStatsAggregator.BuildSummary(_stats, from, to);
            var (periodLabel, dateRangeLabel) = GetPeriodLabels();
            _currentSummary = summary;
            _currentPeriodLabel = periodLabel;
            _currentDateRangeLabel = dateRangeLabel;

            bool hasAnything = summary.TotalSeconds > 0;
            SummaryPanel.Visibility = hasAnything ? Visibility.Visible : Visibility.Collapsed;
            TxtEmptyState.Visibility = hasAnything ? Visibility.Collapsed : Visibility.Visible;
            BtnSaveCard.IsEnabled = hasAnything; // 没数据的时候导出一张全是 0 的卡片没意义，直接禁用按钮
            if (!hasAnything) return;

            TxtTotalTime.Text = ListeningStatsAggregator.FormatDuration(summary.TotalSeconds);
            TxtActiveDays.Text = $"{summary.ActiveDayCount} 天有听歌";

            RenderList(ArtistListPanel, TxtNoArtists, summary.TopArtists.Select(a => (Name: a.Artist, a.Seconds)));
            RenderList(TrackListPanel, TxtNoTracks, summary.TopTracks.Select(t =>
                (Name: string.IsNullOrEmpty(t.Artist) ? t.Title : $"{t.Title} — {t.Artist}", t.Seconds)));
        }

        // 拼卡片 -> 离屏渲染成图片 -> 弹保存对话框让用户自己选存哪、叫什么名字，不替用户擅自决定存放位置
        private void BtnSaveCard_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSummary == null) return;

            try
            {
                var theme = ResolveSelectedCardTheme();
                string? userName = AppSettings.Load().UserName;
                var card = ShareCardBuilder.Build(_currentSummary, _currentPeriodLabel, _currentDateRangeLabel, theme, userName);
                var bitmap = ShareCardBuilder.RenderToBitmap(card);

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "保存分享卡片",
                    Filter = "PNG 图片 (*.png)|*.png",
                    FileName = $"ZipPlay-听歌报告-{_currentPeriodLabel}.png",
                };
                if (dialog.ShowDialog(this) != true) return;

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(dialog.FileName);
                encoder.Save(stream);
            }
            catch (Exception ex)
            {
                // 存图失败（比如目标目录没权限）不是关键功能，记个日志，界面上不用弹刺眼的错误框
                AppLog.Error("ListeningStatsWindow.BtnSaveCard_Click", ex);
            }
        }

        // 艺人榜/歌曲榜是同一套"序号 + 名字 + 时长"行样式，抽成一个方法避免写两遍
        private static void RenderList(StackPanel panel, TextBlock emptyHint, IEnumerable<(string Name, int Seconds)> items)
        {
            panel.Children.Clear();
            var list = items.ToList();
            emptyHint.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            for (int i = 0; i < list.Count; i++)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var name = new TextBlock
                {
                    Text = $"{i + 1}. {list[i].Name}",
                    Foreground = Brushes.White,
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                Grid.SetColumn(name, 0);

                var seconds = new TextBlock
                {
                    Text = ListeningStatsAggregator.FormatDuration(list[i].Seconds),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xA2, 0x96)),
                    FontSize = 11,
                    Margin = new Thickness(8, 0, 0, 0),
                };
                Grid.SetColumn(seconds, 1);

                row.Children.Add(name);
                row.Children.Add(seconds);
                panel.Children.Add(row);
            }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e) => Close();

        // 直接清空，不额外弹确认框——跟设置页"清空歌词缓存"是同一个尺度：清空后不影响正常使用，
        // 只是这份历史记录没了，不是什么不可挽回的重要数据
        private void BtnClearStats_Click(object sender, RoutedEventArgs e)
        {
            ListeningStatsStore.Clear();
            _stats.Days.Clear();
            _stats.Tracks.Clear();
            RefreshSummary();
        }
    }
}
