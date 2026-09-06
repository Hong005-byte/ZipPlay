using System.Linq;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using PixelLyric8BitFix;

namespace PixelLyric8Bit.Mobile;

/// <summary>
/// 自定义主题——精简版：只吃 JSON（跟桌面版自定义主题页同一份格式，能直接互通）；🖌️ 图标画板、
/// 🔗 分享码、文件导入导出都有了，随机生成/混搭这两样还没做。像素图标（icon.rows/icon.frames，含
/// 逐帧动画）悬浮窗那边已经画得出来了，多层装饰（layers）还没有，见 MobileSkinCatalog.FromCustomTheme。
/// 校验用的是 Core 里跟桌面版完全同一份 CustomThemeValidator，报错文案两边一模一样。
///
/// 存取用的是跟 FloatingOverlayService 完全同一份 MobileCustomThemeStore、同一个磁盘目录
/// （FilesDir/custom_themes）——两边各自 new 一个实例出来，不是共享同一个对象引用（本来就是
/// 不同组件），但读写的是同一批文件，这就够了。NavigationCacheMode=Required——编辑到一半的 JSON
/// 不该因为切去别的页面看一眼又切回来就被清空，缓存住这个页面实例正好保留这份没保存的草稿。
///
/// 实时预览（PreviewCard）：照着悬浮窗真实会画出来的样子来——背景/边框/图标（含逐帧动画）/文字色，
/// 不是桌面版 CustomThemeWindow 那张完整播放器卡片，那边还有标题/歌手名两行、歌词框、多层装饰、
/// 8 种内置动画（呼吸发光/摇摆/旋转……）这些精简版悬浮窗压根画不出来的东西，预览里硬做出来反而
/// 会让人以为悬浮窗上也有——诚实地只预览悬浮窗真的会画的这几样。跟桌面版同一个交互套路：改一下
/// JSON（TxtCustomThemeJson_TextChanged）立刻重画，校验没过就把预览换成一行提示，不在这里重复
/// 报具体错误，那是"校验并保存"按钮的事，见 UpdatePreview。
/// </summary>
public sealed partial class CustomThemePage : Page
{
    // 预览的逐帧动画 tick——跟 FloatingOverlayService 的 IconFrameTick 是同一个"tick 累计时间、攒够
    // 一帧的时长才切"思路，这边独立一份计时器，不跟真悬浮窗共用（本来就是两个不同的组件，预览这边
    // 就算悬浮窗没开着也照样能看动画）
    private const int PreviewFrameTickIntervalMs = 100;
    private readonly DispatcherTimer _previewFrameTimer = new() { Interval = TimeSpan.FromMilliseconds(PreviewFrameTickIntervalMs) };
    private WriteableBitmap[]? _previewFrames; // null 或者只有 1 张就是静态图标，tick 直接跳过
    private double _previewFrameDurationSeconds;
    private double _previewFrameElapsedMs;
    private int _previewFrameIndex;

    public CustomThemePage()
    {
        this.InitializeComponent();
        this.NavigationCacheMode = NavigationCacheMode.Required;

        _previewFrameTimer.Tick += (_, _) => PreviewFrameTick();
        _previewFrameTimer.Start();

#if __ANDROID__
        TxtCustomThemeJson.Text = MobileCustomThemeExample.Json; // 触发 TxtCustomThemeJson_TextChanged -> UpdatePreview()，先给一份能直接保存成功、看得到预览的示例，照着改比空白框容易上手
        RefreshCustomThemeList();
#else
        TxtCustomThemeError.Text = "自定义主题：这个功能只在 Android 上有意义";
#endif
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    // 从图标画板点"插入到编辑框"回来的时候，结果放在 IconPainterPage.PendingResultJson 这个静态
    // 字段里（Frame 导航本身没有内建的返回值机制，见 IconPainterPage 顶部注释）。这个页面本身是
    // NavigationCacheMode.Required 缓存的单一实例，构造函数只在第一次创建时跑一遍，所以"回到这个
    // 页面"这件事必须靠 OnNavigatedTo（每次真的导航过来都会触发，不管是不是缓存的实例）来接，用完
    // 立刻清空那个静态字段，不然下次不是从画板回来、只是普通导航过来也会被错误地当成"有新结果"
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (IconPainterPage.PendingResultJson is { } json)
        {
            IconPainterPage.PendingResultJson = null;
            TxtCustomThemeJson.Text = json; // 触发 TxtCustomThemeJson_TextChanged -> UpdatePreview()
        }
    }

    private void BtnOpenIconPainter_Click(object sender, RoutedEventArgs e) =>
        Frame.Navigate(typeof(IconPainterPage), TxtCustomThemeJson.Text);

    // ── 实时预览 ──────────────────────────────────────────────────────────────

    private void TxtCustomThemeJson_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    // 每次改动 JSON 就现算一遍——跟"校验并保存"用的是同一套 CustomThemeValidator.ParseAndValidate，
    // 只有整份 JSON 都过校验才会照着重画预览卡片，校验没过就留白换成提示（不在这里重复报错，具体
    // 错误还是走 BtnSaveCustomTheme_Click 那条路），免得用户还没打完一个字段就被一堆报错轰炸
    private void UpdatePreview()
    {
#if __ANDROID__
        var (theme, errors) = CustomThemeValidator.ParseAndValidate(TxtCustomThemeJson.Text);
        if (theme != null && errors.Count == 0)
        {
            try
            {
                ApplyPreviewTheme(theme);
                PreviewCard.Visibility = Visibility.Visible;
                TxtPreviewHint.Visibility = Visibility.Collapsed;
                return;
            }
            catch
            {
                // 校验都过了理论上不该再炸，真出意外也只是预览留空，不影响正常编辑/保存
            }
        }
#endif
        ClearPreview();
    }

    private void ClearPreview()
    {
        PreviewCard.Visibility = Visibility.Collapsed;
        IconZoomCard.Visibility = Visibility.Collapsed;
        TxtPreviewHint.Visibility = Visibility.Visible;
        _previewFrames = null;
    }

#if __ANDROID__
    // 图标细节放大用的渲染倍数——比默认的 6 大得多，8x8 的图标画出来就是 128x128，放大框
    // （MaxWidth/MaxHeight=220）只需要再放大不到 2 倍就够，不会因为从一张很小的位图硬拉伸到 220px
    // 而糊掉；小图标（PreviewIcon，40x40）从这张位图缩小显示，缩小不会有这个问题，两处共用同一批
    // 位图（见下面 PreviewIcon.Source / IconZoomPreview.Source 都指向同一个数组元素），不是各画一遍
    private const int IconZoomRenderScale = 16;

    // 把校验通过的 CustomTheme 套到预览卡片上——只画悬浮窗真的画得出来的这几样（背景/边框/图标/
    // 文字色），见类顶部注释为什么不照抄桌面版 CustomThemeWindow 那张完整播放器卡片
    private void ApplyPreviewTheme(CustomTheme theme)
    {
        var colors = theme.Colors!;
        CustomThemeValidator.TryParseHexColor(colors.Accent!, out var accent);
        CustomThemeValidator.TryParseHexColor(colors.Lyric!, out var lyric);

        PreviewCard.Background = BuildBackgroundBrush(theme.Background!);
        PreviewCard.BorderBrush = new SolidColorBrush(ToUiColor(accent));
        PreviewText.Foreground = new SolidColorBrush(ToUiColor(lyric));
        PreviewText.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(theme.Font) ? "Consolas" : theme.Font);
        IconZoomCard.BorderBrush = new SolidColorBrush(ToUiColor(accent));

        var palette = CustomThemeValidator.BuildIconPalette(theme.Icon!);
        _previewFrameIndex = 0;
        _previewFrameElapsedMs = 0;

        // 有 Frames 就只认 Frames（跟 FloatingOverlayService.ApplySkin 同一条规则），1 帧当静态图标，
        // >1 帧启动这个页面自己的逐帧 tick；没有 Frames 才用 Rows
        if (theme.Icon!.Frames is { Count: > 0 } frames)
        {
            var frameRows = frames.Select(f => f.ToArray()).ToList();
            var bitmaps = PixelIconRenderer.RenderFrames(frameRows, palette, IconZoomRenderScale);
            PreviewIcon.Source = bitmaps[0];
            IconZoomPreview.Source = bitmaps[0];
            if (bitmaps.Length > 1)
            {
                _previewFrames = bitmaps;
                _previewFrameDurationSeconds = CustomThemeValidator.GetFrameDurationSeconds(theme.Icon);
            }
            else
            {
                _previewFrames = null;
            }
        }
        else
        {
            _previewFrames = null;
            var bitmap = PixelIconRenderer.Render(theme.Icon!.Rows!.ToArray(), palette, IconZoomRenderScale);
            PreviewIcon.Source = bitmap;
            IconZoomPreview.Source = bitmap;
        }

        IconZoomCard.Visibility = Visibility.Visible;
    }

    // 跟桌面版 CustomThemeWindow.BuildBackgroundBrush 是同一个思路（纯色/渐变，渐变支持 vertical/
    // diagonal 两个方向），只是这边用的是 Microsoft.UI.Xaml.Media 的画刷类型，不是 WPF 那套
    private static Brush BuildBackgroundBrush(CustomThemeBackground bg)
    {
        var stops = (bg.Stops ?? new List<string>())
            .Select(s => { CustomThemeValidator.TryParseHexColor(s, out var c); return ToUiColor(c); })
            .ToList();

        if (string.Equals(bg.Type, "gradient", StringComparison.OrdinalIgnoreCase) && stops.Count >= 2)
        {
            var gradient = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = string.Equals(bg.Direction, "diagonal", StringComparison.OrdinalIgnoreCase)
                    ? new Windows.Foundation.Point(1, 1) : new Windows.Foundation.Point(0, 1),
            };
            for (int i = 0; i < stops.Count; i++)
            {
                gradient.GradientStops.Add(new GradientStop { Color = stops[i], Offset = stops.Count == 1 ? 0 : (double)i / (stops.Count - 1) });
            }
            return gradient;
        }
        return new SolidColorBrush(stops.Count > 0 ? stops[0] : Color.FromArgb(255, 0, 0, 0));
    }

    private void PreviewFrameTick()
    {
        var frames = _previewFrames;
        if (frames is not { Length: > 1 }) return;

        _previewFrameElapsedMs += PreviewFrameTickIntervalMs;
        double frameDurationMs = Math.Max(50, _previewFrameDurationSeconds * 1000); // 下限保护，见 FloatingOverlayService.IconFrameTick 同样的注释
        if (_previewFrameElapsedMs < frameDurationMs) return;

        _previewFrameElapsedMs = 0;
        _previewFrameIndex = (_previewFrameIndex + 1) % frames.Length;
        PreviewIcon.Source = frames[_previewFrameIndex];
        IconZoomPreview.Source = frames[_previewFrameIndex]; // 放大框跟主预览同步播放同一帧，见 ApplyPreviewTheme 顶部注释
    }
#endif

    // ── 已存主题存取 ──────────────────────────────────────────────────────────

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
        TxtCustomThemeJson.Text = MobileCustomThemeExample.Json; // 触发 TxtCustomThemeJson_TextChanged -> UpdatePreview()
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

    // ── 🔗 分享码：编辑框里的 JSON ⇄ 一段可以直接发聊天框的纯文本 ──────────────────────
    // 编码/解码本身是 Core 里 CustomThemeShareCode 的活（纯文本互转，不校验），这边只负责"从哪读、
    // 写到哪"——复制走剪贴板，跟桌面版 Clipboard.SetText 是同一个用意，只是 Uno 这边要走
    // Windows.ApplicationModel.DataTransfer 这一套 WinRT 风格的 API，不是直接调一个静态方法。

    private async void BtnCopyShareCode_Click(object sender, RoutedEventArgs e)
    {
#if __ANDROID__
        var (theme, errors) = CustomThemeValidator.ParseAndValidate(TxtCustomThemeJson.Text);
        if (theme == null || errors.Count > 0)
        {
            TxtShareCodeStatus.Text = "编辑框里的 JSON 还没校验通过，先改好格式再复制分享码——不然分享码解出来也是一份存不进去的坏主题。";
            return;
        }

        string code = CustomThemeShareCode.Encode(TxtCustomThemeJson.Text);
        try
        {
            var package = new DataPackage();
            package.SetText(code);
            Clipboard.SetContent(package);
            TxtShareCodeStatus.Text = "分享码已复制到剪贴板，发给别人就行，对方点「粘贴分享码导入」能直接用。";
        }
        catch
        {
            // 剪贴板偶尔会被占用，不是关键功能，失败就把码直接显示出来让用户手动复制
            TxtShareCodeStatus.Text = "剪贴板暂时用不了，把这段分享码手动复制发出去：\n" + code;
        }
#endif
    }

    private async void BtnImportShareCode_Click(object sender, RoutedEventArgs e)
    {
#if __ANDROID__
        string clipboardText;
        try
        {
            var view = Clipboard.GetContent();
            if (!view.Contains(StandardDataFormats.Text))
            {
                TxtShareCodeStatus.Text = "剪贴板里不是文字，先复制一段分享码再点这个按钮。";
                return;
            }
            clipboardText = await view.GetTextAsync();
        }
        catch
        {
            TxtShareCodeStatus.Text = "读不到剪贴板内容，先手动复制一段分享码再试一次。";
            return;
        }

        if (!CustomThemeShareCode.TryDecode(clipboardText, out string json))
        {
            TxtShareCodeStatus.Text = "剪贴板里没找到有效的分享码（得是「ZPT1:」开头那种）。";
            return;
        }

        TxtCustomThemeJson.Text = json; // 触发 TxtCustomThemeJson_TextChanged -> UpdatePreview()，解出来是不是合法主题立刻看得出来
        TxtShareCodeStatus.Text = "已导入到编辑框，看下面预览对不对，确认好了点「校验并保存」才会真的存下来。";
#endif
    }

    // ── 导入 / 导出成文件——系统文件选择器，能存到/读自手机任意位置 ────────────────────
    // Windows.Storage.Pickers 是 WinRT 那套跨平台文件选择器 API，Uno 在 Android 上接的是系统自己的
    // "存储访问框架"（SAF）——弹出来的是系统原生的文件选择界面，不是我们自己画的，这边代码看着
    // 跟桌面版完全一样，具体交互长什么样是系统决定的。

    private async void BtnExportFile_Click(object sender, RoutedEventArgs e)
    {
#if __ANDROID__
        var (theme, errors) = CustomThemeValidator.ParseAndValidate(TxtCustomThemeJson.Text);
        if (theme == null || errors.Count > 0)
        {
            TxtFileIoStatus.Text = "编辑框里的 JSON 还没校验通过，先改好格式再导出——不然导出的文件对方也导不进去。";
            return;
        }

        var picker = new FileSavePicker();
        picker.FileTypeChoices.Add("主题 JSON", new List<string> { ".json" });
        picker.SuggestedFileName = string.IsNullOrWhiteSpace(theme.Name) ? "custom_theme" : theme.Name;

        try
        {
            StorageFile? file = await picker.PickSaveFileAsync();
            if (file == null) return; // 用户自己取消了，不是失败，不用报错

            await FileIO.WriteTextAsync(file, TxtCustomThemeJson.Text);
            TxtFileIoStatus.Text = $"已导出到 {file.Name}。";
        }
        catch (Exception ex)
        {
            TxtFileIoStatus.Text = "导出失败：" + ex.Message;
        }
#endif
    }

    private async void BtnImportFile_Click(object sender, RoutedEventArgs e)
    {
#if __ANDROID__
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".json");

        try
        {
            StorageFile? file = await picker.PickSingleFileAsync();
            if (file == null) return; // 用户自己取消了，不是失败，不用报错

            string content = await FileIO.ReadTextAsync(file);
            TxtCustomThemeJson.Text = content; // 触发 TxtCustomThemeJson_TextChanged -> UpdatePreview()，是不是合法主题立刻看得出来
            TxtFileIoStatus.Text = $"已从 {file.Name} 导入到编辑框，看下面预览对不对，确认好了点「校验并保存」才会真的存下来。";
        }
        catch (Exception ex)
        {
            TxtFileIoStatus.Text = "导入失败：" + ex.Message;
        }
#endif
    }
}
