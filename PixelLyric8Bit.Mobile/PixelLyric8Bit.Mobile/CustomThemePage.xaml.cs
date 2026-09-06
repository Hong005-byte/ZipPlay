using Microsoft.UI.Xaml.Media;
using Windows.UI;
using PixelLyric8BitFix;

namespace PixelLyric8Bit.Mobile;

/// <summary>
/// 自定义主题——精简版：只吃 JSON（跟桌面版自定义主题页同一份格式，能直接互通），没有画板/随机
/// 生成/混搭/分享码这些交互式工具；像素图标（icon.rows/icon.frames，含逐帧动画）悬浮窗那边已经
/// 画得出来了，多层装饰（layers）还没有，见 MobileSkinCatalog.FromCustomTheme。校验用的是 Core
/// 里跟桌面版完全同一份 CustomThemeValidator，报错文案两边一模一样。
///
/// 存取用的是跟 FloatingOverlayService 完全同一份 MobileCustomThemeStore、同一个磁盘目录
/// （FilesDir/custom_themes）——两边各自 new 一个实例出来，不是共享同一个对象引用（本来就是
/// 不同组件），但读写的是同一批文件，这就够了。NavigationCacheMode=Required——编辑到一半的 JSON
/// 不该因为切去别的页面看一眼又切回来就被清空，缓存住这个页面实例正好保留这份没保存的草稿。
/// </summary>
public sealed partial class CustomThemePage : Page
{
    public CustomThemePage()
    {
        this.InitializeComponent();
        this.NavigationCacheMode = NavigationCacheMode.Required;

#if __ANDROID__
        TxtCustomThemeJson.Text = MobileCustomThemeExample.Json; // 先给一份能直接保存成功的示例，照着改比空白框容易上手
        RefreshCustomThemeList();
#else
        TxtCustomThemeError.Text = "自定义主题：这个功能只在 Android 上有意义";
#endif
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

#if __ANDROID__
    private static MobileCustomThemeStore GetCustomThemeStore() => new(
        System.IO.Path.Combine(global::Android.App.Application.Context.FilesDir!.AbsolutePath, "custom_themes"));

    private void RefreshCustomThemeList()
    {
        CustomThemePickerPanel.Children.Clear();
        foreach (var entry in GetCustomThemeStore().ListAll())
        {
            var palette = MobileSkinCatalog.FromCustomTheme(entry.Theme);
            var button = new Button
            {
                Content = palette.DisplayName,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(ToUiColor(palette.Accent)),
                // 皮肤自己配的 Text 色是给深色悬浮窗背景设计的，直接搬来这块用 Accent 当背景的按钮上
                // 经常撞色看不清，见 RgbaColor.PickReadableForeground（跟 SkinPage 同一个坑、同一个修法）
                Foreground = new SolidColorBrush(ToUiColor(RgbaColor.PickReadableForeground(palette.Accent))),
            };
            string skinId = MobileSkinCatalog.CustomThemePrefix + entry.FileName; // 闭包捕获循环变量的经典坑，显式拷贝一份，不然点哪个按钮都会选到最后一个主题
            button.Click += (_, _) => SelectSkin(skinId);
            CustomThemePickerPanel.Children.Add(button);
        }
    }

    private static void SelectSkin(string skinId) =>
        Droid.MobileSettingsStore.SelectedSkinId = skinId; // 悬浮窗（哪怕已经开着）会订阅到这次变化自己换色，见 FloatingOverlayService；皮肤选择页自己的 UpdateCurrentSkinLabel 等下次进那个页面（OnNavigatedTo）自然会重新读到

    private static Color ToUiColor(RgbaColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);
#endif

    private void BtnSaveCustomTheme_Click(object sender, RoutedEventArgs e)
    {
#if __ANDROID__
        var (theme, errors) = CustomThemeValidator.ParseAndValidate(TxtCustomThemeJson.Text);
        if (errors.Count > 0)
        {
            TxtCustomThemeError.Text = string.Join("\n", errors);
            return;
        }

        var (success, error, fileName) = GetCustomThemeStore().Save(theme!);
        if (!success)
        {
            TxtCustomThemeError.Text = error;
            return;
        }

        TxtCustomThemeError.Text = "";
        RefreshCustomThemeList();
        SelectSkin(MobileSkinCatalog.CustomThemePrefix + fileName); // 存完直接切过去用，不用再手动点一下选它
#endif
    }

    private void BtnFillExampleTheme_Click(object sender, RoutedEventArgs e)
    {
#if __ANDROID__
        TxtCustomThemeJson.Text = MobileCustomThemeExample.Json;
        TxtCustomThemeError.Text = "";
#endif
    }

    private void BtnDeleteCustomTheme_Click(object sender, RoutedEventArgs e)
    {
#if __ANDROID__
        string skinId = Droid.MobileSettingsStore.SelectedSkinId;
        if (!skinId.StartsWith(MobileSkinCatalog.CustomThemePrefix, StringComparison.Ordinal))
        {
            TxtCustomThemeError.Text = "当前选中的是内置皮肤，不是自定义主题——先在下面选一个自定义主题再删。";
            return;
        }

        string fileName = skinId[MobileSkinCatalog.CustomThemePrefix.Length..];
        GetCustomThemeStore().Delete(fileName);
        TxtCustomThemeError.Text = "";
        RefreshCustomThemeList();
        SelectSkin(MobileSkinCatalog.DefaultSkinId); // 选中的那个没了，退回默认皮肤，不留一个指向空文件的选择
#endif
    }
}
