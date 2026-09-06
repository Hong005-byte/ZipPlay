using System.Diagnostics;
using PixelLyric8BitFix;

namespace PixelLyric8Bit.Mobile;

/// <summary>
/// 首页——一份功能入口清单（权限与悬浮窗 / 悬浮窗皮肤 / 歌词功能设置 / 听歌统计与成就 / 自定义主题）
/// 加一份示例歌词预览（见 Timer_Tick，用的是 PixelLyric8Bit.Core 里跟桌面版同一份
/// <see cref="PixelLyric8BitFix.LrcParser"/>，不是真正显示歌词的地方）。五个入口各自跳到独立的
/// Page（见 Platforms/Android 之外那几个 *Page.xaml(.cs)），不在这一页里堆内容——之前那版是把
/// 所有设置平铺在一个页面里用按钮展开/收起，页数一多、设置项一多就变成一大坨，现在改成真的
/// Frame 导航，每个功能一个独立页面，回退用 Frame 自带的返回栈（各子页面自己有"‹ 返回"按钮）。
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

    private readonly List<(int TimeMs, string Text)> _lines;
    private readonly DispatcherTimer _timer = new();
    private readonly Stopwatch _stopwatch = new();
    private readonly int _loopDurationMs;

    public MainPage()
    {
        this.InitializeComponent();

        // 首页只会被 Frame.Navigate 创建这一次（子页面 Frame.GoBack 回来的是缓存的同一个实例，
        // 不会重新触发这个构造函数），跟子页面各自设的 NavigationCacheMode 是同一个用意——见
        // PermissionsPage 顶部注释里"设置完手动切回 App 不能丢状态"那一条同样的道理，首页虽然没有
        // 那种跨 App 切换的场景，但重新创建一遍也没意义，保持跟子页面一致
        this.NavigationCacheMode = NavigationCacheMode.Required;

        AppIcon.Source = PixelIconRenderer.Render(NoteIconRows, NoteIconPalette);

        // 骨架阶段先用一份写死的示例 LRC 循环播放；真正的歌词来源接上系统媒体会话之后再替换这一段
        const string sampleLrc = """
            [00:00.00]ZipPlay Mobile 悬浮歌词骨架
            [00:03.00]这行字是 PixelLyric8Bit.Core 的 LrcParser 解析出来的
            [00:07.00]跟桌面版用的是同一份解析逻辑，不是重新写了一遍
            [00:11.00]下一步要接真实的系统媒体会话和悬浮窗权限
            [00:15.50]这一句播完会自动循环回到第一句
            """;
        _lines = PixelLyric8BitFix.LrcParser.ParseLines(sampleLrc);
        _loopDurationMs = (_lines.Count > 0 ? _lines[^1].TimeMs : 0) + 4000; // 最后一句放完留 4 秒再循环，不是切完立刻跳回去

        TxtDynamicLyric.Text = _lines.Count > 0 ? _lines[0].Text : "（示例歌词解析失败）";

        _timer.Interval = TimeSpan.FromMilliseconds(100);
        _timer.Tick += Timer_Tick;
        _stopwatch.Start();
        _timer.Start();
    }

    private void BtnNavPermissions_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(PermissionsPage));

    private void BtnNavSkin_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(SkinPage));

    private void BtnNavLyricFeatures_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(LyricFeaturesPage));

    private void BtnNavStats_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(StatsPage));

    private void BtnNavCustomTheme_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(CustomThemePage));

    private void Timer_Tick(object? sender, object e)
    {
        if (_loopDurationMs <= 0) return;
        int elapsedInLoop = (int)(_stopwatch.ElapsedMilliseconds % _loopDurationMs);

        // 找"时间戳不超过当前播放位置的最后一行"——跟桌面版歌词同步走的是同一个思路
        // （MainWindow.Lyrics.cs 里也是这么找当前该显示哪一行的），只是这里数据源是内存里的示例数据，
        // 不是真实播放位置
        string? current = null;
        foreach (var (timeMs, text) in _lines)
        {
            if (timeMs > elapsedInLoop) break;
            current = text;
        }
        TxtDynamicLyric.Text = current ?? "...";
    }
}
