using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 有新版本时带上：版本号、Release 页面链接（备用，下载失败时可以手动跳转）、
    /// 以及安装包的直链（有的话就能在 app 里一键下载安装，没有就只能引导去网页下载）。
    /// </summary>
    internal record UpdateInfo(string Version, string ReleaseUrl, string? InstallerDownloadUrl);

    /// <summary>
    /// 完整的检查结果：区分"查成功了、没有更新"和"没查成功"（断网/超时/GitHub 抽风），
    /// 后台静默检查用不上这个区分（反正都不弹），但设置页手动点"检查更新"时需要给用户一个准确的提示，
    /// 不能把"没查到"误报成"已是最新版本"。
    /// </summary>
    internal record UpdateCheckResult(bool Success, UpdateInfo? Update);

    /// <summary>
    /// 靠 GitHub Release 做"有没有新版本"的判断：拿 releases/latest 的 tag_name 跟本地程序版本比大小。
    /// 不需要自己搭更新服务器——每次发新版本时在 GitHub 上发一个 Release（tag 形如 v1.2.0），
    /// 把 Inno Setup 打出来的安装包作为附件传上去就行。
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
                    // 404 = 还没发布过任何 Release，这不算"检查失败"，只是确实没有更新可言。
                    // 其它状态码（403 没带 User-Agent / 429 限流 / 5xx GitHub 抽风）是真的没查成功，
                    // 不能也报"已是最新版本"，那是在骗用户。
                    bool noReleaseYet = response.StatusCode == System.Net.HttpStatusCode.NotFound;
                    return new UpdateCheckResult(noReleaseYet, null);
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

                if (latestVersion <= currentVersion)
                {
                    return new UpdateCheckResult(true, null);
                }

                // 找 Release 附件里第一个 .exe（就是 Inno Setup 打出来的安装包），拿它的直链方便一键下载
                string? downloadUrl = null;
                if (obj["assets"] is JArray assets)
                {
                    foreach (var asset in assets)
                    {
                        string? name = asset["name"]?.ToString();
                        if (name != null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset["browser_download_url"]?.ToString();
                            break;
                        }
                    }
                }

                return new UpdateCheckResult(true, new UpdateInfo(versionText, htmlUrl, downloadUrl));
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

        /// <summary>
        /// 把安装包下载到临时目录。下载失败（断网中途断掉等）会把没下完的文件删掉，
        /// 绝不会拿一个残缺的安装包去跑。
        /// </summary>
        public static async Task<string> DownloadInstallerAsync(
            string downloadUrl, HttpClient httpClient, IProgress<double>? progress, CancellationToken token)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), $"ZipPlay-Update-{Guid.NewGuid():N}.exe");

            using var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;

            try
            {
                await using var httpStream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[81920];
                long totalRead = 0;
                int read;
                while ((read = await httpStream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    totalRead += read;
                    if (totalBytes is > 0)
                    {
                        progress?.Report((double)totalRead / totalBytes.Value);
                    }
                }
            }
            catch
            {
                try { File.Delete(tempPath); } catch { /* 尽力清一下就好，删不掉也不影响主流程报错 */ }
                throw;
            }

            return tempPath;
        }

        /// <summary>
        /// 静默跑一遍下载好的安装包（覆盖安装，参数跟手动装一致），然后把当前正在跑的 app 关掉，
        /// 让安装程序能顺利替换掉正在占用的 exe。安装包的 [Run] 小节里配了装完自动重新拉起 app。
        /// </summary>
        public static void LaunchInstallerAndExit(string installerPath)
        {
            Process.Start(new ProcessStartInfo(installerPath)
            {
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
                UseShellExecute = true,
            });

            System.Windows.Application.Current.Shutdown();
        }
    }
}
