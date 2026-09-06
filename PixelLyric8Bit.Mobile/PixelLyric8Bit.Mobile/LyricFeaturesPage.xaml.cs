namespace PixelLyric8Bit.Mobile;

/// <summary>
/// 歌词功能设置：卡拉OK 逐字上色 / 双语歌词 / 同步偏移——对应桌面版这几个开关和滚轮微调，见
/// FloatingOverlayService.cs 顶部说明。三个都是悬浮窗（FloatingOverlayService）实际在用的设置，
/// 这个页面只是个开关面板——改一下 MobileSettingsStore，悬浮窗那边订阅了变更通知会自己跟着生效，
/// 这边不用（也没法）直接摸悬浮窗里的任何状态，两个组件是完全解耦的，见
/// FloatingOverlayService.RefreshSettingsFromStore。
/// </summary>
public sealed partial class LyricFeaturesPage : Page
{
    public LyricFeaturesPage()
    {
        this.InitializeComponent();
        this.NavigationCacheMode = NavigationCacheMode.Required;

#if __ANDROID__
        ChkKaraoke.IsChecked = Droid.MobileSettingsStore.KaraokeEnabled;
        ChkBilingual.IsChecked = Droid.MobileSettingsStore.BilingualEnabled;
        UpdateSyncOffsetLabel(Droid.MobileSettingsStore.SyncOffsetMs);
#else
        ChkKaraoke.IsEnabled = false;
        ChkBilingual.IsEnabled = false;
        TxtSyncOffset.Text = "歌词同步偏移：这个功能只在 Android 上有意义";
#endif
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    private void ChkKaraoke_Toggled(object sender, RoutedEventArgs e)
    {
#if __ANDROID__
        Droid.MobileSettingsStore.KaraokeEnabled = ChkKaraoke.IsChecked == true;
#endif
    }

    private void ChkBilingual_Toggled(object sender, RoutedEventArgs e)
    {
#if __ANDROID__
        Droid.MobileSettingsStore.BilingualEnabled = ChkBilingual.IsChecked == true;
#endif
    }

    private void BtnOffsetMinus_Click(object sender, RoutedEventArgs e) => AdjustSyncOffset(-50);

    private void BtnOffsetPlus_Click(object sender, RoutedEventArgs e) => AdjustSyncOffset(50);

    private void BtnOffsetReset_Click(object sender, RoutedEventArgs e) => AdjustSyncOffset(0, absolute: true);

    private void AdjustSyncOffset(int deltaOrValue, bool absolute = false)
    {
#if __ANDROID__
        int newValue = absolute ? deltaOrValue : Droid.MobileSettingsStore.SyncOffsetMs + deltaOrValue;
        Droid.MobileSettingsStore.SyncOffsetMs = newValue;
        UpdateSyncOffsetLabel(newValue);
#endif
    }

    private void UpdateSyncOffsetLabel(int offsetMs) => TxtSyncOffset.Text = $"歌词同步偏移：{offsetMs}ms";
}
