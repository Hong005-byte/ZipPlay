using System.Diagnostics;

namespace PixelLyric8Bit.Mobile;

/// <summary>
/// 悬浮歌词界面骨架——第一步只做"歌词按时间戳切换"这条链路，用的是 PixelLyric8Bit.Core 里
/// 跟桌面版 MainWindow 同一份 <see cref="PixelLyric8BitFix.LrcParser"/>，证明共享逻辑这条路线
/// 除了编译得过，界面这边接起来也是顺的。
///
/// 现在还没做（按之前讨论的顺序，一步步来）：
/// 1. 真实的系统媒体会话读取（NotificationListenerService + MediaSessionManager），现在歌词来源
///    是写死的示例文本，不是"正在播放的这首歌"。
/// 2. 真正的悬浮窗（SYSTEM_ALERT_WINDOW 悬浮窗权限 + 前台 Service），现在这只是一个普通的全屏页面。
/// 3. 联网抓词（LyricsFetcher 那一套多引擎并发）——桌面版那部分本身就没有 Windows 依赖，理论上
///    能直接搬，但还没验证过。
/// 4. 像素风格的视觉皮肤——现在是一个朴素的深色页面，没有皮肤系统。
/// </summary>
public sealed partial class MainPage : Page
{
    private readonly List<(int TimeMs, string Text)> _lines;
    private readonly DispatcherTimer _timer = new();
    private readonly Stopwatch _stopwatch = new();
    private readonly int _loopDurationMs;

    public MainPage()
    {
        this.InitializeComponent();

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
