using System.Collections.Generic;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 听歌统计的完整数据：按天记录听了多久、听了哪些歌，纯本地，不联网、不上传——
    /// 跟 Mini 模式的音频分析、皮肤音乐律动是同一个隐私立场。
    /// 落盘存取见 <see cref="ListeningStatsStore"/>；月度/年度总结这些"算出来的东西"
    /// 不放在这个类里，见纯逻辑的 <see cref="ListeningStatsAggregator"/>，方便单独写单元测试。
    /// </summary>
    public sealed class ListeningStats
    {
        /// <summary>key 是 "yyyy-MM-dd"（本地时区）。</summary>
        public Dictionary<string, DayStats> Days { get; set; } = new();

        /// <summary>
        /// key 是 trackId（跟 MainWindow._lastTrackId 同一套 "原始标题_原始艺人" 拼法，不做清洗），
        /// 保证跟歌词缓存那边的"这是同一首歌"判断完全一致，不会因为清洗规则不一样而对不上号。
        /// value 存的是清洗过的标题/艺人（去掉 remix/feat 标注、多艺人取第一个），单纯是给报告页面
        /// 显示用的"干净名字"，不参与身份判断。
        /// </summary>
        public Dictionary<string, TrackInfo> Tracks { get; set; } = new();
    }

    public sealed class DayStats
    {
        public int TotalSeconds { get; set; }

        /// <summary>key 是 trackId，value 是这一天听了这首歌多少秒——同一首歌当天可能听了好几遍，累加在一起。</summary>
        public Dictionary<string, int> TrackSeconds { get; set; } = new();
    }

    public sealed class TrackInfo
    {
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
    }
}
