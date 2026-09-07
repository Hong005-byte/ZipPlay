using PixelLyric8BitFix;

namespace PixelLyric8Bit.Mobile;

/// <summary>
/// 首页——一份功能入口清单（权限与悬浮窗 / 悬浮窗皮肤 / 全屏播放 / 歌词功能设置 / 听歌统计与成就 /
/// 自定义主题 / 关于与更新），不带任何示例/演示内容——之前这里还挂了一份写死的示例歌词循环播放
/// （骨架阶段占位用的），跟正式功能没关系，容易让人以为是真在显示什么，删掉了。七个入口各自跳到
/// 独立的 Page（见 Platforms/Android 之外那几个 *Page.xaml(.cs)），不在这一页里堆内容——之前那版是把
/// 所有设置平铺在一个页面里用按钮展开/收起，页数一多、设置项一多就变成一大坨，现在改成真的
/// Frame 导航，每个功能一个独立页面，回退用 Frame 自带的返回栈（各子页面自己有"‹ 返回"按钮）。
///
/// "全屏播放"（FullScreenPlayerPage）是手机版独占的功能，桌面版本来就是一整个窗口，没有"悬浮窗
/// 之外还要不要占满全屏"这个选项，见那个页面顶部注释。
///
/// 悬浮窗真正的实现在 Platforms/Android/FloatingOverlayService.cs（前台 Service + 原生 View，
/// 不是 Uno 渲染的页面），需要用户手动开一次"显示在其他应用上层"权限；系统媒体会话读取
/// （MediaNotificationListenerService）需要手动开一次"通知使用权"——这两块的申请入口都在
/// PermissionsPage，不在这里。
/// </summary>
public sealed partial class MainPage : Page
{
    // 跟桌面版 PixelArt.CreateNoteIcon 完全一样的形状/配色数据（8x8，'#' 画主体、'o' 画点缀细节）——
    // 两边渲染代码各写各的（见 PixelIconRenderer.cs），这份数据本身照抄过来，保证画出来是同一个图标
    private static readonly string[] NoteIconRows =
    {
        "...##...",
        "...##...",
        "...##...",
        "...##o..",
        "..###...",
        ".#####..",
        ".#o###..",
        "..###...",
    };
    private static readonly Dictionary<char, RgbaColor> NoteIconPalette = new()
    {
        ['#'] = new RgbaColor(255, 0x8A, 0xB4, 0xF8),
        ['o'] = new RgbaColor(255, 0xFF, 0xFF, 0xFF),
    };

    public MainPage()
    {
        this.InitializeComponent();

        // 首页只会被 Frame.Navigate 创建这一次（子页面 Frame.GoBack 回来的是缓存的同一个实例，
        // 不会重新触发这个构造函数），跟子页面各自设的 NavigationCacheMode 是同一个用意——见
        // PermissionsPage 顶部注释里"设置完手动切回 App 不能丢状态"那一条同样的道理，首页虽然没有
        // 那种跨 App 切换的场景，但重新创建一遍也没意义，保持跟子页面一致
        this.NavigationCacheMode = NavigationCacheMode.Required;

        AppIcon.Source = PixelIconRenderer.Render(NoteIconRows, NoteIconPalette);
    }

    private void BtnNavPermissions_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(PermissionsPage));

    private void BtnNavSkin_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(SkinPage));

    private void BtnNavFullScreen_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(FullScreenPlayerPage));

    private void BtnNavLyricFeatures_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(LyricFeaturesPage));

    private void BtnNavStats_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(StatsPage));

    private void BtnNavCustomTheme_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(CustomThemePage));

    private void BtnNavAbout_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(AboutPage));
}
