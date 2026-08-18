using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 听歌统计的磁盘存取：整份 <see cref="ListeningStats"/> 存成单个 JSON 文件，
    /// 跟 AppSettings.cs 同一个模式（不是 LyricsCache 那种一首歌一个文件的方式——
    /// 统计数据不是按外部 ID 查单条记录，而是整体读、整体聚合，一次性读写更省事）。
    /// 任何读写失败（文件损坏/没权限/磁盘满）都吞掉退回默认值，不能让统计功能的问题
    /// 影响正常播放——听歌体验优先级永远比"这几秒钟有没有被记进统计"高。
    /// </summary>
    internal static class ListeningStatsStore
    {
        private static string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PixelLyric8BitFix",
            "stats.json");

        public static ListeningStats Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var loaded = JsonConvert.DeserializeObject<ListeningStats>(json);
                    if (loaded != null)
                    {
                        // 反序列化本身没抛异常不代表数据"形状"就一定对——万一文件被手动改过、显式写了
                        // "Days": null 或者某一天的值是 null 这种，Newtonsoft 会照着赋值，把 C# 那边字段
                        // 初始化的默认空字典/空对象直接覆盖掉。这里统一兜一道底、把不合规的条目直接摘掉，
                        // 不然后面每个用到 Days/TrackSeconds 的地方都要单独判空，漏一个就是播放中途 NullReferenceException
                        loaded.Days = (loaded.Days ?? new())
                            .Where(kv => kv.Value != null)
                            .ToDictionary(kv => kv.Key, kv => kv.Value);
                        loaded.Tracks ??= new();
                        foreach (var day in loaded.Days.Values)
                        {
                            day.TrackSeconds ??= new();
                        }
                        return loaded;
                    }
                }
            }
            catch
            {
                // 文件损坏/读不到就当没有统计数据，不影响启动
            }
            return new ListeningStats();
        }

        public static void Save(ListeningStats stats)
        {
            try
            {
                string? dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(stats, Formatting.Indented));
            }
            catch
            {
                // 保存失败（比如没权限、磁盘满）不应该让 App 崩溃或者打断播放，这几秒钟的统计丢了就丢了
            }
        }

        /// <summary>设置页"清空统计数据"按钮用：直接删掉这个文件，下次读就是全新的空统计。</summary>
        public static void Clear()
        {
            try
            {
                if (File.Exists(ConfigPath)) File.Delete(ConfigPath);
            }
            catch
            {
                // 删不掉（比如文件正被占用）不影响正常使用，大不了这份旧数据继续躺着
            }
        }
    }
}
