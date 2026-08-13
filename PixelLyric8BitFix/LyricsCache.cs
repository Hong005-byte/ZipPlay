using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 歌词本地缓存：按 trackId（title_artist）把抓到的 LRC 原文存到磁盘，
    /// 同一首歌下次再放（或者来回切歌切回来）直接命中缓存，不用重新走一遍四引擎并发请求。
    /// 只有校验通过（<see cref="MainWindow"/> 里的 IsDurationPlausible）的歌词才会被写入，
    /// 避免把版本不对、会跑偏的歌词缓存下来污染以后的播放。
    /// </summary>
    internal static class LyricsCache
    {
        // 公开出去给设置页的"打开缓存文件夹"按钮用，方便用户自己肉眼确认这个文件夹里只有 .lrc 缓存文件，
        // 清空按钮也只会动这一个专属子目录，不会碰到 settings.json 或者别的任何东西
        public static string CacheDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PixelLyric8BitFix", "lyrics_cache");

        // trackId 里可能带斜杠、冒号等文件名非法字符，统一哈希成文件名
        private static string PathFor(string trackId)
        {
            string hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(trackId)));
            return Path.Combine(CacheDir, hash + ".lrc");
        }

        public static string? TryGet(string trackId)
        {
            try
            {
                string path = PathFor(trackId);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch
            {
                return null; // 读缓存失败就当没缓存，回落到正常网络抓词流程
            }
        }

        public static void Save(string trackId, string lrcContent)
        {
            if (string.IsNullOrEmpty(lrcContent)) return;
            try
            {
                Directory.CreateDirectory(CacheDir);
                File.WriteAllText(PathFor(trackId), lrcContent);
            }
            catch
            {
                // 写缓存失败（比如没权限、磁盘满）不影响正常播放，下次再抓一次就好
            }
        }

        /// <summary>设置页里"清空歌词缓存"要显示的统计信息：缓存了几首歌、总共占多少空间。</summary>
        public static (int Count, long TotalBytes) GetStats()
        {
            try
            {
                if (!Directory.Exists(CacheDir)) return (0, 0);
                var files = Directory.GetFiles(CacheDir, "*.lrc");
                long total = 0;
                foreach (var f in files) total += new FileInfo(f).Length;
                return (files.Length, total);
            }
            catch
            {
                return (0, 0);
            }
        }

        /// <summary>清空所有本地歌词缓存文件。删了不影响正常使用，下次播放到的歌重新抓一遍就好。</summary>
        public static void Clear()
        {
            try
            {
                if (!Directory.Exists(CacheDir)) return;
                foreach (var f in Directory.GetFiles(CacheDir, "*.lrc"))
                {
                    try { File.Delete(f); } catch { /* 单个文件删不掉（比如被占用）跳过，不影响其它文件 */ }
                }
            }
            catch
            {
                // 整体清理失败不影响正常使用，大不了缓存留着继续占点磁盘空间
            }
        }
    }
}
