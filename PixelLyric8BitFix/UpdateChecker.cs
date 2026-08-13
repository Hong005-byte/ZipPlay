using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PixelLyric8BitFix
{
    /// <summary>有新版本时带上版本号和 Release 页面链接，方便点击直接跳转下载。</summary>
    internal record UpdateInfo(string Version, string ReleaseUrl);

    /// <summary>
    /// 完整的检查结果：区分"查成功了、没有更新"和"没查成功"（断网/超时/GitHub 抽风），
    /// 后台静默检查用不上这个区分（反正都不弹），但设置页手动点"检查更新"时需要给用户一个准确的提示，
    /// 不能把"没查到"误报成"已是最新版本"。
    /// </summary>
    internal record UpdateCheckResult(bool Success, UpdateInfo? Update);

    /// <summary>
    /// 靠 GitHub Release 做"有没有新版本"的判断：拿 releases/latest 的 tag_name 跟本地程序版本比大小。
    /// 不需要自己搭更新服务器——每次发新版本时在 GitHub 上发一个 Release（tag 形如 v1.2.0）就行。
    /// </summary>
    internal static class UpdateChecker
    {
        // 仓库改名/搬家了记得改这里
        private const string ReleasesApiUrl = "https://api.github.com/repos/Hong005-byte/ZipPlay/releases/latest";

        public static async Task<UpdateCheckResult> CheckAsync(Version currentVersion, HttpClient httpClient)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApiUrl);
                request.Headers.Accept.ParseAdd("application/vnd.github+json");

                using var response = await httpClient.SendAsync(request).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    // 还没发布过任何 Release 时 GitHub 会返回 404——这不算"检查失败"，
                    // 只是确实没有更新可言，跟检查成功、当前已是最新版本是一回事
                    return new UpdateCheckResult(true, null);
                }

                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var obj = JObject.Parse(json);

                string? tag = obj["tag_name"]?.ToString();
                string? htmlUrl = obj["html_url"]?.ToString();
                if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(htmlUrl))
                {
                    return new UpdateCheckResult(true, null);
                }

                string versionText = tag.TrimStart('v', 'V');
                if (!Version.TryParse(versionText, out var latestVersion))
                {
                    return new UpdateCheckResult(true, null);
                }

                var update = latestVersion > currentVersion ? new UpdateInfo(versionText, htmlUrl) : null;
                return new UpdateCheckResult(true, update);
            }
            catch
            {
                // 断网、超时、GitHub 抽风：检查这件事本身没做成，不等于"已是最新版本"
                return new UpdateCheckResult(false, null);
            }
        }

        /// <summary>后台静默检查用的简化版：反正查不到也不弹提示，成功/失败不用区分。</summary>
        public static async Task<UpdateInfo?> CheckForUpdateAsync(Version currentVersion, HttpClient httpClient)
        {
            var result = await CheckAsync(currentVersion, httpClient).ConfigureAwait(false);
            return result.Update;
        }
    }
}
