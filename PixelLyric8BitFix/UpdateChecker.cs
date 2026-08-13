using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PixelLyric8BitFix
{
    /// <summary>检查结果：有新版本时带上版本号和 Release 页面链接，方便点击直接跳转下载。</summary>
    internal record UpdateInfo(string Version, string ReleaseUrl);

    /// <summary>
    /// 靠 GitHub Release 做"有没有新版本"的判断：拿 releases/latest 的 tag_name 跟本地程序版本比大小。
    /// 不需要自己搭更新服务器——每次发新版本时在 GitHub 上发一个 Release（tag 形如 v1.2.0）就行。
    /// </summary>
    internal static class UpdateChecker
    {
        // 仓库改名/搬家了记得改这里
        private const string ReleasesApiUrl = "https://api.github.com/repos/Hong005-byte/ZipPlay/releases/latest";

        public static async Task<UpdateInfo?> CheckForUpdateAsync(Version currentVersion, HttpClient httpClient)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApiUrl);
                request.Headers.Accept.ParseAdd("application/vnd.github+json");

                using var response = await httpClient.SendAsync(request).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    // 还没发布过任何 Release 时 GitHub 会返回 404，这很正常，安静跳过就好
                    return null;
                }

                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var obj = JObject.Parse(json);

                string? tag = obj["tag_name"]?.ToString();
                string? htmlUrl = obj["html_url"]?.ToString();
                if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(htmlUrl)) return null;

                string versionText = tag.TrimStart('v', 'V');
                if (!Version.TryParse(versionText, out var latestVersion)) return null;

                return latestVersion > currentVersion ? new UpdateInfo(versionText, htmlUrl) : null;
            }
            catch
            {
                // 断网、超时、GitHub 抽风都不该影响正常使用，检查更新失败就当没这回事
                return null;
            }
        }
    }
}
