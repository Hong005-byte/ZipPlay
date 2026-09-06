using System.Diagnostics;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using PixelLyric8BitFix;

namespace PixelLyric8Bit.Mobile;

/// <summary>
/// 悬浮歌词界面骨架——这个页面本身还是用写死的示例 LRC 循环播放（见 Timer_Tick），用的是
/// PixelLyric8Bit.Core 里跟桌面版同一份 <see cref="PixelLyric8BitFix.LrcParser"/>；这里只是个
/// 权限状态验证面板，不是真正显示歌词的地方。
///
/// 页面上是两块 Android 专属能力的验证区（对应的实现在 Platforms/Android/ 下，`#if __ANDROID__`
/// 隔开，非 Android 平台编译时这些代码直接被预处理器去掉）：
/// - 系统媒体会话读取（MediaNotificationListenerService）——桌面版 SMTC 的对应物，读"现在系统里
///   随便哪个 App 正在播放什么"，需要用户去系统设置手动开一次"通知使用权"。
/// - 真正的悬浮窗（FloatingOverlayService）——一个前台 Service 用 IWindowManager 加一个能拖动的
///   原生 View，不是 Uno 渲染的页面；需要用户手动开一次"显示在其他应用上层"权限。
///
/// 这两块的"读到的真实播放信息接进悬浮窗里显示"这一步已经在 FloatingOverlayService 里做完了——
/// 订阅媒体会话的播放状态/元数据变化事件、联网抓真实歌词、按 PlaybackPositionEstimator 插值出的
/// 播放位置显示对应那一行，见该文件顶部注释。这个页面（MainPage）本身显示的还是假数据，纯粹是
/// 因为它只是个"两个特殊权限开没开、悬浮窗要不要显示"的开关面板，跟悬浮窗是两个独立的 UI，没有
/// 必要接同一份真实歌词——除非以后想在 App 主界面里也做一个跟悬浮窗同步的歌词视图。
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

        RefreshMediaSessionStatus();
        RefreshOverlayStatus();
        BuildSkinPicker();
        BuildLyricFeatureSettings();
        BuildListeningStatsSection();
        BuildCustomThemeSection();
    }

    // ── 悬浮窗皮肤（精简版：只挑配色，见 MobileSkinPalette.cs） ───────────────────────

    private void BuildSkinPicker()
    {
#if __ANDROID__
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
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(locked ? Color.FromArgb(255, 0x33, 0x33, 0x33) : ToUiColor(palette.Accent)),
                Foreground = new SolidColorBrush(locked ? Color.FromArgb(255, 0x88, 0x88, 0x88) : ToUiColor(palette.Text)),
                IsEnabled = !locked,
            };
            string id = palette.Id; // 闭包捕获循环变量的经典坑，显式拷贝一份，不然点哪个按钮都会选到最后一个皮肤
            if (!locked) button.Click += (_, _) => SelectSkin(id);
            SkinPickerPanel.Children.Add(button);
        }
        UpdateCurrentSkinLabel(selectedId);
#else
        TxtCurrentSkin.Text = "悬浮窗皮肤：这个功能只在 Android 上有意义";
#endif
    }

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

    // ── 自定义主题（精简版：只吃 JSON，见 MainPage.xaml 顶部说明） ────────────────────────
    // 存取用的是跟 FloatingOverlayService 完全同一份 MobileCustomThemeStore、同一个磁盘目录
    // （FilesDir/custom_themes）——两边各自 new 一个实例出来，不是共享同一个对象引用（本来就是
    // 不同组件），但读写的是同一批文件，这就够了。

#if __ANDROID__
    private static MobileCustomThemeStore GetCustomThemeStore() => new(
        System.IO.Path.Combine(global::Android.App.Application.Context.FilesDir!.AbsolutePath, "custom_themes"));
#endif

    private void BuildCustomThemeSection()
    {
#if __ANDROID__
        TxtCustomThemeJson.Text = MobileCustomThemeExample.Json; // 先给一份能直接保存成功的示例，照着改比空白框容易上手
        RefreshCustomThemeList();
#else
        TxtCustomThemeError.Text = "自定义主题：这个功能只在 Android 上有意义";
#endif
    }

#if __ANDROID__
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
                Foreground = new SolidColorBrush(ToUiColor(palette.Text)),
            };
            string skinId = MobileSkinCatalog.CustomThemePrefix + entry.FileName; // 闭包捕获，见 BuildSkinPicker 同样的注释
            button.Click += (_, _) => SelectSkin(skinId);
            CustomThemePickerPanel.Children.Add(button);
        }
    }
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
        SelectSkin(MobileSkinCatalog.CustomThemePrefix + fileName); // 存完直接切过去用，看得到效果，不用再手动点一下选它
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

    // ── 歌词功能设置：卡拉OK 逐字上色 / 双语歌词 / 同步偏移 ─────────────────────────────
    // 三个都是悬浮窗（FloatingOverlayService）实际在用的设置，这个页面只是个开关面板——改一下
    // MobileSettingsStore，悬浮窗那边订阅了变更通知会自己跟着生效，这边不用（也没法）直接摸悬浮窗
    // 里的任何状态，两个组件是完全解耦的，见 FloatingOverlayService.RefreshSettingsFromStore。

    private void BuildLyricFeatureSettings()
    {
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

    // ── 听歌统计 + 成就墙（精简版：几个核心数字 + 一份 checklist，见 MainPage.xaml 顶部说明） ──────
    // 统计数据是 FloatingOverlayService 在悬浮窗那边攒、存盘的，这个页面完全不采集——两边是不同的
    // 组件（Service vs. Page），没有共享内存状态，只有同一份磁盘文件是两边的共同点，所以这里用定时器
    // 隔几秒重新读一次文件，不是订阅什么变更通知（跟皮肤那种"同进程内 SharedPreferences 监听"不是
    // 一回事，读文件本身足够便宜，没必要为了这个再多做一层通知机制）。

    private DispatcherTimer? _statsRefreshTimer;

    private void BuildListeningStatsSection()
    {
#if __ANDROID__
        _statsRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _statsRefreshTimer.Tick += (_, _) => RefreshListeningStats();
        _statsRefreshTimer.Start();
        RefreshListeningStats();
#else
        TxtListeningSummary.Text = "听歌统计：这个功能只在 Android 上有意义";
#endif
    }

#if __ANDROID__
    private static string GetStatsFilePath() =>
        System.IO.Path.Combine(global::Android.App.Application.Context.FilesDir!.AbsolutePath, "stats.json");
#endif

    private void RefreshListeningStats()
    {
#if __ANDROID__
        var stats = new ListeningStatsFileStore(GetStatsFilePath()).Load();

        int totalSeconds = PixelLyric8BitFix.ListeningStatsAggregator.GetTotalSeconds(stats, DateOnly.MinValue, DateOnly.MaxValue);
        int activeDays = PixelLyric8BitFix.ListeningStatsAggregator.GetActiveDayCount(stats, DateOnly.MinValue, DateOnly.MaxValue);
        int longestStreak = PixelLyric8BitFix.ListeningStatsAggregator.GetLongestStreakDays(stats);

        TxtListeningSummary.Text = totalSeconds > 0
            ? $"听歌统计：总时长 {PixelLyric8BitFix.ListeningStatsAggregator.FormatDuration(totalSeconds)} · 活跃 {activeDays} 天 · 最长连续 {longestStreak} 天"
            : "听歌统计：还没有数据（悬浮窗打开、真的在放歌的时候才会开始攒）";

        BuildAchievementRows(stats);
#endif
    }

#if __ANDROID__
    private void BuildAchievementRows(PixelLyric8BitFix.ListeningStats stats)
    {
        AchievementsPanel.Children.Clear();
        foreach (var progress in PixelLyric8BitFix.AchievementCalculator.Evaluate(stats))
        {
            var row = new TextBlock
            {
                Text = $"{(progress.Unlocked ? "✅" : "🔒")} {progress.Achievement.Icon} {progress.Achievement.Name}"
                    + $" —— {progress.Achievement.Description}",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = new SolidColorBrush(progress.Unlocked ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 0x55, 0x55, 0x55)),
            };
            AchievementsPanel.Children.Add(row);
        }
    }
#endif

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

        // 顺手每 tick 刷一下两个权限的状态——骨架阶段图省事，用定时轮询代替事件通知，真正做的话
        // 应该是 MediaController.Callback / Activity 生命周期事件主动推变化过来，不是页面自己每
        // 100ms 问一次。这两个权限都是跳系统设置页申请的，用户设置完手动切回 App 之后 MainPage
        // 这个实例并不会被重新创建（只是从后台切回前台），不放这里刷的话状态会一直停在"跳转去设置
        // 那一刻"，哪怕权限其实已经开了，界面还是显示"未开启"——真的踩过这个坑（悬浮窗权限开了，
        // 界面死活不更新，adb appops 查底层记录才发现权限其实早就是 allow 了，是界面没刷新）。
        RefreshMediaSessionStatus();
        RefreshOverlayStatus();
    }

    // ── 系统媒体会话（通知使用权） ────────────────────────────────────────

    private void RefreshMediaSessionStatus()
    {
#if __ANDROID__
        var context = global::Android.App.Application.Context;
        if (!Droid.MediaNotificationListenerService.IsListenerAccessGranted(context))
        {
            TxtMediaSessionStatus.Text = "通知使用权：未开启（没有这个权限读不到系统正在播放什么）";
            BtnOpenNotificationSettings.Visibility = Visibility.Visible;
            return;
        }

        BtnOpenNotificationSettings.Visibility = Visibility.Collapsed;
        var service = Droid.MediaNotificationListenerService.Instance;
        if (service == null)
        {
            TxtMediaSessionStatus.Text = "通知使用权：已开启（服务还没连接上，稍等一下或者重开一次 App）";
            return;
        }

        var controllers = service.GetActiveMediaControllers();
        if (controllers.Count == 0)
        {
            TxtMediaSessionStatus.Text = "通知使用权：已开启，但现在没有检测到正在播放的 App";
            return;
        }

        var metadata = controllers[0].Metadata;
        string title = metadata?.GetString(global::Android.Media.MediaMetadata.MetadataKeyTitle) ?? "（无标题）";
        string artist = metadata?.GetString(global::Android.Media.MediaMetadata.MetadataKeyArtist) ?? "";
        TxtMediaSessionStatus.Text = "通知使用权：已开启，正在播放：" + title + (string.IsNullOrEmpty(artist) ? "" : $" - {artist}");
#else
        TxtMediaSessionStatus.Text = "通知使用权：这个功能只在 Android 上有意义";
        BtnOpenNotificationSettings.Visibility = Visibility.Collapsed;
#endif
    }

    private void BtnOpenNotificationSettings_Click(object sender, RoutedEventArgs e)
    {
#if __ANDROID__
        Droid.MediaNotificationListenerService.OpenListenerAccessSettings(global::Android.App.Application.Context);
#endif
    }

    // ── 悬浮窗权限 ─────────────────────────────────────────────────────

    private void RefreshOverlayStatus()
    {
#if __ANDROID__
        var context = global::Android.App.Application.Context;
        bool granted = Droid.FloatingOverlayService.CanDrawOverlays(context);
        TxtOverlayStatus.Text = granted
            ? (Droid.FloatingOverlayService.IsRunning ? "悬浮窗权限：已开启，悬浮窗正在显示" : "悬浮窗权限：已开启")
            : "悬浮窗权限：未开启（没有这个权限没法在别的 App 上层显示歌词）";
        BtnOpenOverlaySettings.Visibility = granted ? Visibility.Collapsed : Visibility.Visible;
        BtnToggleOverlay.IsEnabled = granted;
        BtnToggleOverlay.Content = Droid.FloatingOverlayService.IsRunning ? "隐藏悬浮窗" : "显示悬浮窗";
#else
        TxtOverlayStatus.Text = "悬浮窗权限：这个功能只在 Android 上有意义";
        BtnOpenOverlaySettings.Visibility = Visibility.Collapsed;
        BtnToggleOverlay.IsEnabled = false;
#endif
    }

    private void BtnOpenOverlaySettings_Click(object sender, RoutedEventArgs e)
    {
#if __ANDROID__
        Droid.FloatingOverlayService.OpenOverlaySettings(global::Android.App.Application.Context);
#endif
    }

    private void BtnToggleOverlay_Click(object sender, RoutedEventArgs e)
    {
#if __ANDROID__
        var context = global::Android.App.Application.Context;
        if (Droid.FloatingOverlayService.IsRunning)
        {
            Droid.FloatingOverlayService.Stop(context);
        }
        else
        {
            Droid.FloatingOverlayService.Start(context);
        }
        RefreshOverlayStatus();
#endif
    }
}
