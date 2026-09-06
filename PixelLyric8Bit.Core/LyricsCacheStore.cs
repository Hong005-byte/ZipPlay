using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 歌词本地缓存：按 trackId 把抓到的 LRC 原文（+ 翻译）存到磁盘，同一首歌下次再放（或者来回切歌
    /// 切回来）直接命中缓存，不用重新走一遍多引擎并发请求。逻辑跟桌面版 PixelLyric8BitFix/LyricsCache.cs
    /// 完全一样（同一套哈希文件名方案），唯一的区别是这边不是静态类、缓存目录是构造函数传进来的——
    /// 桌面版用 Environment.SpecialFolder.ApplicationData 就行，Android 那边"App 私有目录在哪"得从
    /// Context.CacheDir 问系统要，两边拿目录的方式不一样，索性把目录做成参数，存取这部分纯逻辑就能
    /// 两边共用（这也是没有直接把桌面版那个静态类整个搬过来、而是照着重新写一份实例版的原因）。
    /// </summary>
    public sealed class LyricsCacheStore
    {
        private readonly string _cacheDir;

        public LyricsCacheStore(string cacheDir) => _cacheDir = cacheDir;

        // trackId 里可能带斜杠、冒号等文件名非法字符，统一哈希成文件名。原文跟翻译存成同一个哈希、
        // 不同后缀，一眼能看出是同一首歌的两份文件
        private static string HashFor(string trackId) =>
            Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(trackId)));

        private string PathFor(string trackId) => Path.Combine(_cacheDir, HashFor(trackId) + ".lrc");
        private string TranslationPathFor(string trackId) => Path.Combine(_cacheDir, HashFor(trackId) + ".tlrc");

        public string? TryGet(string trackId)
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

        public void Save(string trackId, string lrcContent)
        {
            if (string.IsNullOrEmpty(lrcContent)) return;
            try
            {
                Directory.CreateDirectory(_cacheDir);
                File.WriteAllText(PathFor(trackId), lrcContent);
            }
            catch
            {
                // 写缓存失败（比如没权限、磁盘满）不影响正常播放，下次再抓一次就好
            }
        }

        public string? TryGetTranslation(string trackId)
        {
            try
            {
                string path = TranslationPathFor(trackId);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch
            {
                return null;
            }
        }

        public void SaveTranslation(string trackId, string translationLrcContent)
        {
            if (string.IsNullOrEmpty(translationLrcContent)) return;
            try
            {
                Directory.CreateDirectory(_cacheDir);
                File.WriteAllText(TranslationPathFor(trackId), translationLrcContent);
            }
            catch
            {
                // 同上，写失败不影响正常使用
            }
        }

        /// <summary>缓存了几首歌、总共占多少空间（原文 + 翻译）——给"清空缓存"这类设置页用。</summary>
        public (int Count, long TotalBytes) GetStats()
        {
            try
            {
                if (!Directory.Exists(_cacheDir)) return (0, 0);
                var lrcFiles = Directory.GetFiles(_cacheDir, "*.lrc");
                var tlrcFiles = Directory.GetFiles(_cacheDir, "*.tlrc");
                long total = 0;
                foreach (var f in lrcFiles) total += new FileInfo(f).Length;
                foreach (var f in tlrcFiles) total += new FileInfo(f).Length;
                return (lrcFiles.Length, total); // Count 只算原文份数（一首歌一份），翻译只是附属，不重复计数
            }
            catch
            {
                return (0, 0);
            }
        }

        /// <summary>清空所有本地歌词缓存文件（原文 + 翻译）。删了不影响正常使用，下次播放到的歌重新抓一遍就好。</summary>
        public void Clear()
        {
            try
            {
                if (!Directory.Exists(_cacheDir)) return;
                foreach (var f in Directory.GetFiles(_cacheDir, "*.lrc").Concat(Directory.GetFiles(_cacheDir, "*.tlrc")))
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
