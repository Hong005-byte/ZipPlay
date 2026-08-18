using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 皮肤主题选择页：从 HomeWindow 的"🎨 皮肤主题"格子进来。打开时按 AppSettings 里存的选择勾好卡片，
    /// 关闭（不管是点"返回首页"还是直接按 X）的时候把选中的皮肤存回去——跟 CustomThemeWindow/
    /// ListeningStatsWindow 一样是"自己开自己关、自己管自己那部分状态"的独立小窗口，不需要一个
    /// 全局的"保存"按钮来统一收口。
    /// </summary>
    public partial class SkinPickerWindow : Window
    {
        // 客制化主题的卡片 Tag 是 "Custom:文件名" 这种复合格式（PlayerSkin.Custom 这一个枚举值被多个不同文件共用，
        // 光靠枚举名分不清是哪一个），内置皮肤还是走 Tag == 枚举名那套
        private const string CustomTagPrefix = "Custom:";

        // 客制化主题卡片是动态加到 SkinPanel 末尾的（不是 XAML 写死的那些），记着引用方便刷新时先摘掉旧的
        private readonly List<RadioButton> _customThemeCards = new();

        public SkinPickerWindow()
        {
            InitializeComponent();

            var settings = AppSettings.Load();

            RefreshCrownLockState();

            foreach (var child in SkinPanel.Children)
            {
                // 加一道 rb.IsEnabled 才能勾——不然限定皮肤被重新锁住之后（比如统计数据被清空过），
                // 之前存的 settings.Skin 还是它，这里会把一张已经禁用的卡片程序化地设成"选中"，
                // 用户什么都没做，光是打开又关掉这个页面就把锁住的皮肤重新存回去了
                if (child is RadioButton rb && rb.Tag is string tag && Enum.TryParse<PlayerSkin>(tag, out var skin))
                {
                    rb.IsChecked = skin == settings.Skin && rb.IsEnabled;
                }
            }
            RefreshCustomThemeCards(settings);

            Closed += (s, e) =>
            {
                var toSave = AppSettings.Load();
                toSave.Skin = GetSelectedSkin();
                toSave.CustomThemeFile = GetSelectedCustomThemeFile();
                toSave.Save();
            };
        }

        // 限定"尊贵皇冠"皮肤解不解锁是从听歌统计现算出来的（没有独立的持久化状态），每次打开这个页面
        // 都重新判断一遍——万一统计数据被清空过，卡片会自动退回锁住的状态
        private void RefreshCrownLockState()
        {
            var stats = ListeningStatsStore.Load();
            var (crownUnlocked, remaining) = AchievementCalculator.EvaluateCrownLock(stats);
            RbSkinCrown.IsEnabled = crownUnlocked;
            RbSkinCrown.ToolTip = crownUnlocked
                ? "已解锁"
                : $"还差 {remaining} 个成就解锁，去首页「🏆 成就墙」看看";
        }

        private PlayerSkin GetSelectedSkin()
        {
            foreach (var child in SkinPanel.Children)
            {
                // IsEnabled 这道检查是双保险——正常情况下锁住的卡片压根不会被设成 IsChecked=true
                // （见构造函数），这里再挡一道，防的是"万一以后哪里不小心 checked 了一张禁用卡片"
                if (child is RadioButton { IsChecked: true, IsEnabled: true } rb && rb.Tag is string tag)
                {
                    if (tag.StartsWith(CustomTagPrefix)) return PlayerSkin.Custom;
                    if (Enum.TryParse<PlayerSkin>(tag, out var skin)) return skin;
                }
            }
            return PlayerSkin.Minecraft;
        }

        private string? GetSelectedCustomThemeFile()
        {
            foreach (var child in SkinPanel.Children)
            {
                if (child is RadioButton { IsChecked: true } rb && rb.Tag is string tag && tag.StartsWith(CustomTagPrefix))
                {
                    return tag.Substring(CustomTagPrefix.Length);
                }
            }
            return null;
        }

        // 把上次加过的客制化卡片先摘掉，再按最新存的主题列表重新加一遍（新建/编辑/删除之后都要调用这个刷新）
        private void RefreshCustomThemeCards(AppSettings settings)
        {
            foreach (var old in _customThemeCards) SkinPanel.Children.Remove(old);
            _customThemeCards.Clear();

            foreach (var entry in CustomThemeStore.ListAll())
            {
                var rb = new RadioButton
                {
                    Style = (Style)FindResource("SkinCardStyle"),
                    GroupName = "Skin",
                    Tag = CustomTagPrefix + entry.FileName,
                    IsChecked = settings.Skin == PlayerSkin.Custom && settings.CustomThemeFile == entry.FileName,
                };

                CustomThemeValidator.TryParseHexColor(entry.Theme.Colors?.Accent ?? "#55FF55", out var accent);
                var stops = entry.Theme.Background?.Stops ?? new List<string>();
                Color c1 = accent, c2 = accent;
                if (stops.Count > 0) CustomThemeValidator.TryParseHexColor(stops[0], out c1);
                if (stops.Count > 0) CustomThemeValidator.TryParseHexColor(stops[^1], out c2);

                rb.Background = new LinearGradientBrush(c1, c2, 90);
                rb.BorderBrush = new SolidColorBrush(accent);
                rb.Content = new TextBlock { Text = "🎨 " + (entry.Theme.Name ?? "自定义"), Style = (Style)FindResource("SkinCardLabelStyle") };

                SkinPanel.Children.Add(rb);
                _customThemeCards.Add(rb);
            }
        }

        private void BtnManageCustomThemes_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new CustomThemeWindow { Owner = this };
            dlg.ShowDialog();
            if (dlg.ThemesChanged)
            {
                RefreshCustomThemeCards(AppSettings.Load());
            }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e) => Close();
    }
}
