namespace PixelLyric8Bit.Mobile;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        this.InitializeComponent();

        // 冒烟测试：不只是编译得过，真的跑一遍 PixelLyric8Bit.Core 里的歌词解析逻辑（命名空间还是
        // PixelLyric8BitFix，见 PixelLyric8Bit.Core 那次搬迁的说明），验证"共享大脑"这条路线真的通。
        // 这几行以后接上真正的悬浮歌词 UI 就可以删掉了，纯粹是这一步的验证代码。
        const string sampleLrc = "[00:01.00]第一行\n[00:05.50]第二行\n[00:09.20]第三行";
        var lastTimestamp = PixelLyric8BitFix.LrcParser.GetLastTimestamp(sampleLrc);
        HelloTextBlock.Text = lastTimestamp is { } ts
            ? $"Core 共享逻辑跑通了：最后一行时间戳 {ts.TotalSeconds:0.0}s"
            : "Core 共享逻辑解析失败";
    }
}
