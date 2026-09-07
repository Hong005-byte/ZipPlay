using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PixelLyric8BitFix
{
    /// <summary>有新版本时带上：版本号、Release 页面链接（备用，下载失败时可以手动跳去网页下）、
    /// 以及 APK 直链（有的话就能在 app 里下载后直接唤出系统安装器，没有就只能引导去网页下载）。</summary>
    public sealed record MobileUpdateInfo(string Version, string ReleaseUrl, string? ApkDownloadUrl);

    /// <summary>完整的检查结果：区分"查成功了、没有更新"和"没查成功"（断网/超时/GitHub 抽风）——
    /// 跟桌面版 UpdateCheckResult 是同一个用意，见该类型注释。</summary>
    public sealed record MobileUpdateCheckResult(bool Success, MobileUpdateInfo? Update);

    /// <summary>
    /// 手机版有没有新版本可用——跟桌面版 UpdateChecker 一样靠 GitHub Release 判断，但不共用它查
    /// "releases/latest" 那条路：桌面版每次发版本会把 tag 打成 "vX.Y.Z"，如果手机版也直接查 /latest，
    /// 一旦某次只发了桌面版更新（Release 里没附 .apk），手机端会把那次的 tag_name 当成"手机版也该
    /// 更新到这个版本号"去跟自己的版本号比——两边版本号体系本来就不是一回事，比出来的结果没有意义，
    /// 轻则误报"检查失败"，重则误报"有更新"却点开下载不到 apk。
    ///
    /// 改成手机版自己的 tag 前缀（"mobile-v" 开头，比如 "mobile-v1.0.0"），查 /releases 列表（不是
    /// /latest 那个单条端点），按时间从新到旧找第一个这个前缀的 tag——桌面版、手机版各发各的
    /// Release，共用同一个 GitHub 仓库完全没问题，两条检查逻辑互不干扰。发布手机版新版本时记得
    /// 照这个前缀打 tag，见 README「发布新版本」那节。
    /// </summary>
    public static class MobileUpdateChecker
    {
        // 仓库改名/搬家了记得改这里——跟桌面版 UpdateChecker.ReleasesApiUrl 是同一个仓库，只是这边
        // 查的是列表端点不是 /latest
        private const string ReleasesListApiUrl = "https://api.github.com/repos/Hong005-byte/ZipPlay/releases";
        private const string MobileTagPrefix = "mobile-v";

        public static async Task<MobileUpdateCheckResult> CheckAsync(Version currentVersion, HttpClient httpClient)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesListApiUrl);
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                // GitHub API 对匿名请求要求带 User-Agent，缺了会直接 403——桌面版那边走的是
                // NetworkHelpers 建出来的 HttpClient，可能已经带了别的默认头，这边手机端直接在这
                // 一条请求上补，不依赖调用方传进来的 HttpClient 有没有配置过默认头
                request.Headers.UserAgent.TryParseAdd("ZipPlayMobile-UpdateChecker");

                using var response = await httpClient.SendAsync(request).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    // 404 通常是仓库本身查不到（理论不该发生），不算"检查失败"；其它状态码（403 没
                    // 带对头 / 429 限流 / 5xx GitHub 抽风）是真的没查成功，不能报"已是最新版本"
                    bool notFoundIsOkay = response.StatusCode == System.Net.HttpStatusCode.NotFound;
                    return new MobileUpdateCheckResult(notFoundIsOkay, null);
                }

                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var releases = JArray.Parse(json);

                foreach (var release in releases)
                {
                    string? tag = release["tag_name"]?.ToString();
                    if (string.IsNullOrEmpty(tag) || !tag.StartsWith(MobileTagPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // 桌面版的 Release（或者别的什么 tag），跳过去找下一条
                    }

                    string? htmlUrl = release["html_url"]?.ToString();
                    if (string.IsNullOrEmpty(htmlUrl)) continue;

                    string versionText = tag[MobileTagPrefix.Length..];
                    if (!Version.TryParse(versionText, out var latestVersion)) continue;

                    if (latestVersion <= currentVersion)
                    {
                        return new MobileUpdateCheckResult(true, null); // 找到的第一条手机版 Release 已经是当前版本或更旧，没有更新
                    }

                    // 找 Release 附件里第一个 .apk，拿它的直链方便在 app 里直接下载
                    string? apkUrl = null;
                    if (release["assets"] is JArray assets)
                    {
                        foreach (var asset in assets)
                        {
                            string? name = asset["name"]?.ToString();
                            if (name != null && name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                            {
                                apkUrl = asset["browser_download_url"]?.ToString();
                                break;
                            }
                        }
                    }

                    return new MobileUpdateCheckResult(true, new MobileUpdateInfo(versionText, htmlUrl, apkUrl));
                }

                // 列表里翻完了也没有任何 "mobile-v" 前缀的 tag——还没发过手机版 Release，不算检查失败
                return new MobileUpdateCheckResult(true, null);
            }
            catch
            {
                // 断网、超时、GitHub 抽风：检查这件事本身没做成，不等于"已是最新版本"
                return new MobileUpdateCheckResult(false, null);
            }
        }

        /// <summary>后台静默检查用的简化版：反正查不到也不弹提示，成功/失败不用区分——跟桌面版
        /// UpdateChecker.CheckForUpdateAsync 是同一个用意。</summary>
        public static async Task<MobileUpdateInfo?> CheckForUpdateAsync(Version currentVersion, HttpClient httpClient)
        {
            var result = await CheckAsync(currentVersion, httpClient).ConfigureAwait(false);
            return result.Update;
        }
    }
}
