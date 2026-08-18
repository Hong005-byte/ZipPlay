using System;
using System.Collections.Generic;
using System.Linq;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 听歌统计的纯计算部分：给一份 <see cref="ListeningStats"/> + 时间范围，算出总时长、活跃天数、
    /// 热门艺人、热门歌曲。不碰磁盘、不碰 UI，方便直接写单元测试——跟 KaraokeTiming/LrcParser
    /// 是同一个套路（纯函数 + internal static，独立于负责 I/O 的 ListeningStatsStore）。
    /// </summary>
    internal static class ListeningStatsAggregator
    {
        public static int GetTotalSeconds(ListeningStats stats, DateOnly from, DateOnly to)
        {
            int total = 0;
            foreach (var (dayKey, day) in stats.Days)
            {
                if (TryParseDayKey(dayKey, out var date) && date >= from && date <= to)
                {
                    total += day.TotalSeconds;
                }
            }
            return total;
        }

        public static int GetActiveDayCount(ListeningStats stats, DateOnly from, DateOnly to)
        {
            int count = 0;
            foreach (var (dayKey, day) in stats.Days)
            {
                if (day.TotalSeconds > 0 && TryParseDayKey(dayKey, out var date) && date >= from && date <= to)
                {
                    count++;
                }
            }
            return count;
        }

        public static List<ArtistStat> GetTopArtists(ListeningStats stats, DateOnly from, DateOnly to, int topN)
        {
            var byArtist = new Dictionary<string, int>();
            foreach (var (trackId, seconds) in EnumerateTrackSecondsInRange(stats, from, to))
            {
                string artist = stats.Tracks.TryGetValue(trackId, out var info) && !string.IsNullOrWhiteSpace(info.Artist)
                    ? info.Artist
                    : "未知艺人"; // trackId 理论上都该有对应的 TrackInfo（HandleTrackChangeAsync 里同一次切歌就注册了），这里兜个底避免万一对不上时直接崩
                byArtist[artist] = byArtist.GetValueOrDefault(artist) + seconds;
            }
            return byArtist
                .OrderByDescending(kv => kv.Value)
                .Take(topN)
                .Select(kv => new ArtistStat { Artist = kv.Key, Seconds = kv.Value })
                .ToList();
        }

        public static List<TrackStat> GetTopTracks(ListeningStats stats, DateOnly from, DateOnly to, int topN)
        {
            var byTrack = new Dictionary<string, int>();
            foreach (var (trackId, seconds) in EnumerateTrackSecondsInRange(stats, from, to))
            {
                byTrack[trackId] = byTrack.GetValueOrDefault(trackId) + seconds;
            }
            return byTrack
                .OrderByDescending(kv => kv.Value)
                .Take(topN)
                .Select(kv =>
                {
                    stats.Tracks.TryGetValue(kv.Key, out var info);
                    return new TrackStat
                    {
                        Title = !string.IsNullOrWhiteSpace(info?.Title) ? info!.Title : kv.Key,
                        Artist = info?.Artist ?? "",
                        Seconds = kv.Value,
                    };
                })
                .ToList();
        }

        /// <summary>报告页面直接绑定用的打包结果，一次算齐总时长/活跃天数/前 N 艺人/前 N 歌曲。</summary>
        public static ListeningSummary BuildSummary(ListeningStats stats, DateOnly from, DateOnly to, int topN = 5) => new()
        {
            TotalSeconds = GetTotalSeconds(stats, from, to),
            ActiveDayCount = GetActiveDayCount(stats, from, to),
            TopArtists = GetTopArtists(stats, from, to, topN),
            TopTracks = GetTopTracks(stats, from, to, topN),
        };

        /// <summary>时长转成"N 小时 M 分钟"这种人话——HomeWindow 首页卡片、听歌统计页、分享卡片三处
        /// 都要用同一份文案，不然会出现同一个数字三个地方显示不一样的情况（之前就真的出现过：首页显示
        /// "1.5 小时"，统计页显示"1 小时 30 分钟"，是同一个 totalSeconds 算出来的）。</summary>
        public static string FormatDuration(int totalSeconds)
        {
            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            if (hours > 0) return $"{hours} 小时 {minutes} 分钟";
            if (minutes > 0) return $"{minutes} 分钟";
            return "不到 1 分钟";
        }

        /// <summary>最长连续听歌天数（全部时间范围内，不局限于当前还在不在连续）——给成就墙的
        /// "连续 N 天"这类成就用。掉了一天就断，不是"总共听了多少天"那种松散计法。</summary>
        public static int GetLongestStreakDays(ListeningStats stats)
        {
            var activeDays = stats.Days
                .Where(kv => kv.Value.TotalSeconds > 0)
                .Select(kv => TryParseDayKey(kv.Key, out var d) ? d : (DateOnly?)null)
                .Where(d => d.HasValue)
                .Select(d => d!.Value)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            if (activeDays.Count == 0) return 0;

            int longest = 1, current = 1;
            for (int i = 1; i < activeDays.Count; i++)
            {
                current = activeDays[i] == activeDays[i - 1].AddDays(1) ? current + 1 : 1;
                if (current > longest) longest = current;
            }
            return longest;
        }

        /// <summary>听过的不同艺人数（全部时间）——基于 Days.TrackSeconds 里真的出现过秒数的 trackId，
        /// 不是 Tracks 表本身的条数：Tracks 只要切歌切到过这首歌就会注册一条，哪怕只听了半秒钟就跳走，
        /// 用它算"听过多少艺人"会虚高，达不到"这是个要花时间才能拿到的成就"的意图。</summary>
        public static int GetUniqueArtistCount(ListeningStats stats) =>
            EnumerateTrackSecondsInRange(stats, DateOnly.MinValue, DateOnly.MaxValue)
                .Select(t => t.TrackId)
                .Distinct()
                .Select(id => stats.Tracks.TryGetValue(id, out var info) ? info.Artist : null)
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct()
                .Count();

        /// <summary>听过的不同歌曲数（全部时间），同样只算真的攒到过听歌秒数的 trackId。</summary>
        public static int GetUniqueTrackCount(ListeningStats stats) =>
            EnumerateTrackSecondsInRange(stats, DateOnly.MinValue, DateOnly.MaxValue)
                .Select(t => t.TrackId)
                .Distinct()
                .Count();

        // 同一首歌可能横跨好几天听、同一天也可能听了好几遍（DayStats.TrackSeconds 已经把同一天的
        // 累加过了），这里只管把范围内每一天、每个 trackId 的秒数原样吐出来，交给上面两个方法按
        // 艺人/歌曲各自分组求和
        private static IEnumerable<(string TrackId, int Seconds)> EnumerateTrackSecondsInRange(ListeningStats stats, DateOnly from, DateOnly to)
        {
            foreach (var (dayKey, day) in stats.Days)
            {
                if (!TryParseDayKey(dayKey, out var date) || date < from || date > to) continue;
                foreach (var (trackId, seconds) in day.TrackSeconds)
                {
                    yield return (trackId, seconds);
                }
            }
        }

        private static bool TryParseDayKey(string dayKey, out DateOnly date) =>
            DateOnly.TryParseExact(dayKey, "yyyy-MM-dd", out date);
    }

    public sealed class ListeningSummary
    {
        public int TotalSeconds { get; set; }
        public int ActiveDayCount { get; set; }
        public List<ArtistStat> TopArtists { get; set; } = new();
        public List<TrackStat> TopTracks { get; set; } = new();
    }

    public sealed class ArtistStat
    {
        public string Artist { get; set; } = "";
        public int Seconds { get; set; }
    }

    public sealed class TrackStat
    {
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public int Seconds { get; set; }
    }
}
