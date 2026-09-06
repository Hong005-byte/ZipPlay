using Android.Content;

namespace PixelLyric8Bit.Mobile.Droid;

/// <summary>
/// 本地设置的读写——桌面版对应的是 AppSettings.cs（那边存的是一份 JSON 文件）。Android 上没有必要
/// 自己再搭一套文件读写，SharedPreferences 就是这类"一堆零散小设置项"的标准存法，系统自己管持久化，
/// 换个方式还更省心。
///
/// 静态方法而不是实例——设置本身就是"整个 App 只有一份"的东西（跟桌面版 AppSettings 是同一个模型），
/// MainPage（设置页）和 FloatingOverlayService（悬浮窗，读取当前皮肤）两边都要用，不需要互相持有
/// 对方的引用，各自调静态方法就行。
/// </summary>
public static class MobileSettingsStore
{
    private const string PrefsName = "zipplay_mobile_settings";
    private const string KeySelectedSkin = "selected_skin_id";
    private const string KeySyncOffsetMs = "sync_offset_ms";
    private const string KeyKaraokeEnabled = "karaoke_enabled";
    private const string KeyBilingualEnabled = "bilingual_enabled";

    private static ISharedPreferences Prefs =>
        global::Android.App.Application.Context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!;

    public static string SelectedSkinId
    {
        get => Prefs.GetString(KeySelectedSkin, PixelLyric8BitFix.MobileSkinCatalog.DefaultSkinId)
            ?? PixelLyric8BitFix.MobileSkinCatalog.DefaultSkinId;
        set
        {
            using var editor = Prefs.Edit()!;
            editor.PutString(KeySelectedSkin, value);
            editor.Apply(); // 异步落盘就行，不是什么必须马上写进去、写失败要处理的关键数据
        }
    }

    /// <summary>歌词同步偏移，单位 ms——对应桌面版鼠标滚轮微调那个设置（每格 50ms）。系统汇报的播放
    /// 位置跟实际听感之间有固有延迟，这个数会加到"用来找当前该显示哪一行"的位置上，不影响真实的
    /// 播放位置本身。</summary>
    public static int SyncOffsetMs
    {
        get => Prefs.GetInt(KeySyncOffsetMs, 0);
        set
        {
            using var editor = Prefs.Edit()!;
            editor.PutInt(KeySyncOffsetMs, value);
            editor.Apply();
        }
    }

    /// <summary>卡拉OK 逐字扫光开关——对应桌面版齿轮旁边那个开关，关掉退回整行切换。</summary>
    public static bool KaraokeEnabled
    {
        get => Prefs.GetBoolean(KeyKaraokeEnabled, true); // 默认开，跟桌面版默认行为一致
        set
        {
            using var editor = Prefs.Edit()!;
            editor.PutBoolean(KeyKaraokeEnabled, value);
            editor.Apply();
        }
    }

    /// <summary>双语歌词开关——对应桌面版左上角 🌐 开关，开了原文下面多显示一行翻译。</summary>
    public static bool BilingualEnabled
    {
        get => Prefs.GetBoolean(KeyBilingualEnabled, false); // 默认关，跟桌面版默认行为一致
        set
        {
            using var editor = Prefs.Edit()!;
            editor.PutBoolean(KeyBilingualEnabled, value);
            editor.Apply();
        }
    }

    /// <summary>设置变化时想收到通知的地方（目前只有 FloatingOverlayService 用，皮肤在悬浮窗开着的
    /// 时候被换了要跟着重画）注册这个监听——SharedPreferences 自带的变更通知机制，不用自己另外搭一套
    /// 事件总线。</summary>
    public static void RegisterChangeListener(ISharedPreferencesOnSharedPreferenceChangeListener listener) =>
        Prefs.RegisterOnSharedPreferenceChangeListener(listener);

    public static void UnregisterChangeListener(ISharedPreferencesOnSharedPreferenceChangeListener listener) =>
        Prefs.UnregisterOnSharedPreferenceChangeListener(listener);
}
