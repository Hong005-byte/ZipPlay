using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;
using PixelLyric8BitFix;

namespace PixelLyric8Bit.Mobile;

/// <summary>
/// 悬浮窗皮肤选择——精简版：内置皮肤配色 + 像素图标，客制化主题的 icon.rows/icon.frames 也画得出来
/// （见 FloatingOverlayService.ApplySkin），不是桌面版那套带多层装饰 + 专属律动的完整皮肤系统，
/// 见 MobileSkinPalette.cs 顶部注释。选好之后存到 MobileSettingsStore，悬浮窗（哪怕已经开着）
/// 会跟着立刻换色，不用重新打开悬浮窗才生效。
///
/// NavigationCacheMode=Required——不这样设的话每次从首页点进来都会重新创建一遍页面；但页面本身
/// 缓存不代表内容不会过期——皮肤列表跟着自定义主题存档走，那份存档是 CustomThemePage 在管，用户
/// 完全可能"进这页看一眼 -> 返回 -> 去自定义主题页存/删一个 -> 再进这页"，缓存的旧按钮列表就对不上
/// 最新的存档了。所以按钮列表的构建放在 OnNavigatedTo（每次真的导航到这个页面都会触发，不管页面
/// 实例是不是缓存的），不是只在构造函数里跑一次，见 UpdateCurrentSkinLabel 同样要查自定义主题名字。
///
/// BuildSkinPicker 现在也会把已存的客制化主题列进来（之前这里漏掉了，只画了内置皮肤——存好的
/// 自定义主题只能回自定义主题页自己那条横向选择条里选，这一页压根看不到，见 CustomThemePage.
/// RefreshCustomThemeList 那边一直有类似列表，这边补上同一份逻辑），跟内置皮肤共用同一个竖排
/// 列表，一条分隔文字隔开。
/// </summary>
public sealed partial class SkinPage : Page
{
    public SkinPage()
    {
        this.InitializeComponent();
        this.NavigationCacheMode = NavigationCacheMode.Required;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        BuildSkinPicker();
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    private void BuildSkinPicker()
    {
#if __ANDROID__
        SkinPickerPanel.Children.Clear(); // 每次导航过来都重新画一遍，见类顶部注释——不清空的话旧按钮会跟新按钮叠在一起
        string selectedId = Droid.MobileSettingsStore.SelectedSkinId;

        // 尊贵皇冠风是限定皮肤——桌面版要先在成就墙点亮全部 7 个常规听歌成就才解锁，这是这套皮肤
        // 唯一的获取方式，见 AchievementCalculator.CrownSkin 的注释。Mobile 这边阶段 4 已经在攒同一份
        // 听歌统计了，理应遵守同一条规则，不能让手机这边随便点一下就绕过桌面版特意设计的"很难拿到"
        var stats = new ListeningStatsFileStore(GetStatsFilePath()).Load();
        var (crownUnlocked, crownRemaining) = AchievementCalculator.EvaluateCrownLock(stats);

        foreach (var palette in MobileSkinCatalog.All)
        {
            bool locked = palette.Id == "Crown" && !crownUnlocked;

            var button = new Button
            {
                Content = locked ? $"🔒 {palette.DisplayName}（还差 {crownRemaining} 个成就）" : palette.DisplayName,
                Margin = new Thickness(0, 0, 0, 8), // 竖着往下排，间距挪到下边，不是右边
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(locked ? Color.FromArgb(255, 0x33, 0x33, 0x33) : ToUiColor(palette.Accent)),
                // 按钮背景是皮肤的 Accent 色，不是悬浮窗那个深色背景——皮肤自己配的 Text 色是给深色
                // 背景设计的，直接搬过来经常跟 Accent 撞色看不清，见 RgbaColor.PickReadableForeground
                Foreground = new SolidColorBrush(locked ? Color.FromArgb(255, 0x88, 0x88, 0x88) : ToUiColor(RgbaColor.PickReadableForeground(palette.Accent))),
                IsEnabled = !locked,
            };
            string id = palette.Id; // 闭包捕获循环变量的经典坑，显式拷贝一份，不然点哪个按钮都会选到最后一个皮肤
            if (!locked) button.Click += (_, _) => SelectSkin(id);
            SkinPickerPanel.Children.Add(button);
        }

        // 已存的客制化主题（CustomThemePage 那边存/删）——之前这里漏掉了，只列内置皮肤，存好的自定义
        // 主题只能在自定义主题页自己那条横向选择条里选，这一页完全看不到、选不到。跟内置皮肤共用
        // 同一个竖排列表，用一条分隔文字隔开；一个都没存过就不画这一段，不留一段没用的空标题
        var customThemes = GetCustomThemeStore().ListAll();
        if (customThemes.Count > 0)
        {
            SkinPickerPanel.Children.Add(new TextBlock
            {
                Text = "🖌️ 自定义主题",
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0xAA, 0xAA, 0xAA)),
                FontSize = 13,
                Margin = new Thickness(0, 10, 0, 8),
            });

            foreach (var entry in customThemes)
            {
                var palette = MobileSkinCatalog.FromCustomTheme(entry.Theme);
                var button = new Button
                {
                    Content = palette.DisplayName,
                    Margin = new Thickness(0, 0, 0, 8),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Background = new SolidColorBrush(ToUiColor(palette.Accent)),
                    Foreground = new SolidColorBrush(ToUiColor(RgbaColor.PickReadableForeground(palette.Accent))),
                };
                string skinId = MobileSkinCatalog.CustomThemePrefix + entry.FileName; // 闭包坑，同上，显式拷贝一份
                button.Click += (_, _) => SelectSkin(skinId);
                SkinPickerPanel.Children.Add(button);
            }
        }

        UpdateCurrentSkinLabel(selectedId);
#else
        TxtCurrentSkin.Text = "悬浮窗皮肤：这个功能只在 Android 上有意义";
#endif
    }

#if __ANDROID__
    private static string GetStatsFilePath() =>
        System.IO.Path.Combine(global::Android.App.Application.Context.FilesDir!.AbsolutePath, "stats.json");

    private static MobileCustomThemeStore GetCustomThemeStore() => new(
        System.IO.Path.Combine(global::Android.App.Application.Context.FilesDir!.AbsolutePath, "custom_themes"));
#endif

    private void SelectSkin(string skinId)
    {
#if __ANDROID__
        Droid.MobileSettingsStore.SelectedSkinId = skinId; // 悬浮窗（哪怕已经开着）会订阅到这次变化自己换色，见 FloatingOverlayService
        UpdateCurrentSkinLabel(skinId);
#endif
    }

    private void UpdateCurrentSkinLabel(string skinId)
    {
#if __ANDROID__
        // "custom:文件名" 得去自定义主题存档里查名字，内置表里根本没有这个 id——Find 兜底回第一套
        // 皮肤的名字会显示成错的（比如明明选的是自定义主题，标签却显示"简约风"）
        if (skinId.StartsWith(MobileSkinCatalog.CustomThemePrefix, StringComparison.Ordinal))
        {
            string fileName = skinId[MobileSkinCatalog.CustomThemePrefix.Length..];
            var theme = GetCustomThemeStore().Load(fileName);
            TxtCurrentSkin.Text = $"悬浮窗皮肤：{(theme?.Name ?? "（自定义主题，已被删除）")}";
            return;
        }
#endif
        var palette = MobileSkinCatalog.Find(skinId);
        TxtCurrentSkin.Text = $"悬浮窗皮肤：{palette.DisplayName}";
    }

    private static Color ToUiColor(RgbaColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);
}
