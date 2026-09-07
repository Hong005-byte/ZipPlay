using System.Net.Http;
using Android.Content;
using AndroidX.Core.Content;

namespace PixelLyric8Bit.Mobile.Droid;

/// <summary>
/// 把 MobileUpdateChecker 查到的 APK 直链下载到本地、再交给系统包安装器——Android 没有桌面版那个
/// "/VERYSILENT 静默跑安装包"的路，这一步用户必须自己在系统安装器上点一下"安装"/"更新"确认，
/// 这是系统安全机制卡的，不是这边没做完。能自动化的只有"下载"这一步和"帮用户把系统安装器界面
/// 唤出来"这两件事，跟桌面版 UpdateChecker.DownloadInstallerAsync + LaunchInstallerAndExit 是同一个
/// 分工，只是最后一步没法做到"装完自动退出重启"那么彻底。
///
/// content:// URI 是必须的——Android 7（API 24）起 StrictMode 默认禁止把 file:// URI 分享给别的
/// App（包括系统包安装器自己），得通过 FileProvider（见 AndroidManifest.xml 里的 &lt;provider&gt;
/// 和 Resources/xml/file_paths.xml 那份路径映射）换一个 content:// URI 出来，直接把 file:// 塞给
/// Intent.ActionView 在新版 Android 上会直接抛 FileUriExposedException 崩溃。
/// </summary>
internal static class AndroidUpdateInstaller
{
    /// <summary>下载到 CacheDir/update/ 下——跟歌词缓存同一个"缓存目录、系统缺存储空间可能被自动
    /// 清掉也无所谓"的语义，装完这个 APK 之后留着也没用，不用特意去清。下载失败（断网中途断掉等）
    /// 会把没下完的文件删掉，绝不会拿一个残缺的安装包去跑，跟桌面版同名方法是同一条规则。</summary>
    public static async Task<string> DownloadApkAsync(
        string downloadUrl, HttpClient httpClient, IProgress<double>? progress, CancellationToken token)
    {
        var context = global::Android.App.Application.Context;
        string dir = Path.Combine(context.CacheDir!.AbsolutePath, "update");
        Directory.CreateDirectory(dir);
        // 固定文件名（不是每次一个新 Guid）——上一次下载残留的旧安装包不用特意清理，这次直接覆盖，
        // 也省得 CacheDir/update/ 底下越攒越多没人清的文件
        string path = Path.Combine(dir, "ZipPlay-Update.apk");

        using var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        long? totalBytes = response.Content.Headers.ContentLength;

        try
        {
            await using var httpStream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);

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
            try { File.Delete(path); } catch { /* 尽力清一下就好，删不掉也不影响主流程报错 */ }
            throw;
        }

        return path;
    }

    /// <summary>唤出系统包安装器——跳去装刚下载好的 APK，用户自己确认"安装"/"更新"。这个 App 还没有
    /// REQUEST_INSTALL_PACKAGES 权限（"允许安装未知来源应用"，特殊权限，AndroidManifest.xml 里已经
    /// 声明了，但跟悬浮窗/通知使用权那两个一样得用户自己去系统设置手动开一次）的话，系统会自己弹一个
    /// "去设置开启"的引导页，不用我们额外判断这件事、也不用在这边自己再实现一遍状态检查。</summary>
    public static void LaunchInstaller(string apkPath)
    {
        var context = global::Android.App.Application.Context;
        var apkFile = new Java.IO.File(apkPath);
        var uri = FileProvider.GetUriForFile(context, context.PackageName + ".fileprovider", apkFile);

        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(uri, "application/vnd.android.package-archive");
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
        context.StartActivity(intent);
    }
}
