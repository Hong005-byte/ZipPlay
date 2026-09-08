namespace PixelLyric8Bit.Mobile;

/// <summary>手机版当前版本号——检查更新（AboutPage/MobileUpdateChecker）拿这个跟 GitHub Release 的
/// tag 比大小。不读 Android 系统层面的 PackageInfo.VersionName（省得为了一个字符串常量去处理
/// PackageManager 在不同 API level 上那几个重载的兼容性问题，这个值本来就该跟下面第 2 条手动保持
/// 一致，两边不一致本身就是没做好发布流程，不是运行时才发现的问题）。发布新版本时要跟这三处一起改，
/// 不然要么显示的版本号不对，要么检查更新永远查不到"有更新"（本地这个常量没跟着提升）：
/// 1. 这个常量本身
/// 2. PixelLyric8Bit.Mobile.csproj 的 &lt;ApplicationDisplayVersion&gt;（保持跟这个字符串一致，
///    Android 系统层面（比如系统应用信息页显示的版本号）读的是那边，不是这个常量）
/// 3. 发 Release 时打的 tag（"mobile-v" + 这个版本号，比如版本是 "1.2.0" 就打 "mobile-v1.2.0"，
///    见 MobileUpdateChecker 顶部注释、README「发布新版本」那节）</summary>
public static class AppVersion
{
    public const string Current = "1.1.0";
}
