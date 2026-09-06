using Newtonsoft.Json;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 听歌统计的磁盘存取——逻辑照抄桌面版 PixelLyric8BitFix/ListeningStatsStore.cs（整份 ListeningStats
    /// 存成单个 JSON 文件，任何读写失败都吞掉退回默认值，不能让统计功能的问题影响正常播放），唯一区别
    /// 是文件路径是构造函数参数不是写死的 ApplicationData 路径——跟 LyricsCacheStore 同一个理由：Android
    /// 那边"App 私有目录在哪"得从 Context 问系统要，两边拿路径的方式不一样，索性做成参数两边共用。
    /// 类名不叫 ListeningStatsStore 是因为桌面项目直接引用 Core、又在同一个命名空间下，撞名会导致
    /// "到底用哪个 ListeningStatsStore" 的二义性编译错误，见 LyricsTranslator.cs 顶部注释同样的教训。
    /// </summary>
    public sealed class ListeningStatsFileStore
    {
        private readonly string _filePath;

        public ListeningStatsFileStore(string filePath) => _filePath = filePath;

        public ListeningStats Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    var loaded = JsonConvert.DeserializeObject<ListeningStats>(json);
                    if (loaded != null)
                    {
                        // 反序列化本身没抛异常不代表数据"形状"就一定对——万一文件被手动改过、显式写了
                        // "Days": null 这种，Newtonsoft 会照着赋值，把默认空字典覆盖掉。这里统一兜一道底，
                        // 不然后面每个用到 Days/TrackSeconds 的地方都要单独判空
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

        public void Save(ListeningStats stats)
        {
            try
            {
                string? dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_filePath, JsonConvert.SerializeObject(stats, Formatting.Indented));
            }
            catch
            {
                // 保存失败（比如没权限、磁盘满）不应该影响正常播放，这几秒钟的统计丢了就丢了
            }
        }

        /// <summary>"清空统计数据"用：直接删掉这个文件，下次读就是全新的空统计。</summary>
        public void Clear()
        {
            try
            {
                if (File.Exists(_filePath)) File.Delete(_filePath);
            }
            catch
            {
                // 删不掉（比如文件正被占用）不影响正常使用，大不了这份旧数据继续躺着
            }
        }
    }
}
