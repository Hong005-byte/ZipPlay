using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PixelLyric8BitFix
{
    /// <summary>抓词结果：原文 LRC 是必有的，翻译 LRC 目前只有网易云那个引擎会给（其它三个引擎接口本身不带翻译）。</summary>
    internal sealed record LyricsFetchResult(string Lrc, string? TranslationLrc);

    /// <summary>
    /// 多引擎并发抓词：LRCLIB / 网易云 / QQ音乐 / 酷狗，谁先给出"时长对得上"的结果就用谁。
    /// 从 MainWindow 里搬出来单独成类——纯粹是"给个歌名/歌手/期望时长，要么给我一份能用的 LRC，
    /// 要么给 null"，不碰 UI、不碰缓存，以后改抓词逻辑或者单独测试都不用在主窗口那个大文件里翻。
    /// </summary>
    internal sealed class LyricsFetcher
    {
        private readonly HttpClient _httpClient;

        public LyricsFetcher(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        // 四引擎并发抓词，谁先给出时长对得上的结果就用谁；全部落空（或者版本都对不上）返回 null
        public async Task<LyricsFetchResult?> FetchAsync(string title, string artist, TimeSpan expectedDuration, CancellationToken token)
        {
            var pending = new List<Task<(string? Lrc, string? Translation)>>
            {
                FetchFromLrcLibAsync(title, artist, token),
                FetchFromNeteaseAsync(title, artist, token),
                FetchFromQQMusicAsync(title, artist, token),
                FetchFromKugouAsync(title, artist, token),
            };

            while (pending.Count > 0)
            {
                var finished = await Task.WhenAny(pending);
                pending.Remove(finished);

                if (token.IsCancellationRequested) return null; // 已经切歌了，这个结果作废

                (string? lrc, string? translation) = (null, null);
                try { (lrc, translation) = await finished; } catch { /* 单引擎异常已在内部吞掉，这里兜底 */ }

                if (string.IsNullOrEmpty(lrc)) continue;

                // 网上同一首歌常常有好几个版本的 LRC（原版/加速版/Remix/电台剪辑），
                // 抓错版本的话时长对不上，歌词会越走越偏。拿系统报告的真实播放时长交叉校验一下，
                // 明显对不上就当这个引擎没抓到，换下一个，而不是硬塞一份会跑偏的歌词。
                if (!IsDurationPlausible(lrc, expectedDuration)) continue;

                return new LyricsFetchResult(lrc, translation);
            }

            return null;
        }

        // 用 LRC 里最后一句歌词的时间戳粗略估算"这份歌词是多长的版本"，
        // 跟系统报告的真实播放时长做个交叉验证。放宽到 75%，是为了不误伤那些
        // 最后一句歌词离结尾还有一段纯音乐尾奏的正常歌曲。
        // 公开成 static：MainWindow 校验本地缓存命中的那份歌词是否还适用时也要用同一套逻辑。
        public static bool IsDurationPlausible(string lrcContent, TimeSpan expectedDuration)
        {
            if (expectedDuration <= TimeSpan.Zero) return true; // 系统没报时长，没法校验，别拦

            var lastTimestamp = LrcParser.GetLastTimestamp(lrcContent);
            if (lastTimestamp == null || lastTimestamp.Value <= TimeSpan.Zero) return true; // 解析不出来也别拦

            double ratio = lastTimestamp.Value.TotalSeconds / expectedDuration.TotalSeconds;
            return ratio >= 0.75;
        }

        // 1) LRCLIB —— 完全开放、免费、无需 key，同步歌词质量高，优先级最高
        private async Task<(string? Lrc, string? Translation)> FetchFromLrcLibAsync(string title, string artist, CancellationToken token)
        {
            try
            {
                string url = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
                string json = await _httpClient.GetStringAsync(url, token);
                return (JObject.Parse(json)["syncedLyrics"]?.ToString(), null);
            }
            catch { return (null, null); }
        }

        // 2) 网易云音乐（非官方接口）—— 中文歌曲命中率高；四个引擎里唯一会给"翻译歌词"（tlyric）的一个，
        //    外语歌配的中文翻译行，跟原文歌词是同一种 LRC 格式、只是时间戳独立一份
        private async Task<(string? Lrc, string? Translation)> FetchFromNeteaseAsync(string title, string artist, CancellationToken token)
        {
            try
            {
                string searchUrl = $"https://music.163.com/api/search/get/web?s={Uri.EscapeDataString(title + " " + artist)}&type=1&limit=1";
                string searchJson = await _httpClient.GetStringAsync(searchUrl, token);
                string? songId = JObject.Parse(searchJson)["result"]?["songs"]?[0]?["id"]?.ToString();
                if (string.IsNullOrEmpty(songId)) return (null, null);

                string lyricUrl = $"https://music.163.com/api/song/lyric?os=pc&id={songId}&lv=-1&kv=-1&tv=-1";
                string lyricJson = await _httpClient.GetStringAsync(lyricUrl, token);
                var obj = JObject.Parse(lyricJson);
                string? lrc = obj["lrc"]?["lyric"]?.ToString();
                string? translation = obj["tlyric"]?["lyric"]?.ToString();
                return (lrc, string.IsNullOrWhiteSpace(translation) ? null : translation);
            }
            catch { return (null, null); }
        }

        // 3) QQ音乐（非官方接口）—— 需要带 Referer 否则会被拒绝
        private async Task<(string? Lrc, string? Translation)> FetchFromQQMusicAsync(string title, string artist, CancellationToken token)
        {
            try
            {
                string keyword = Uri.EscapeDataString($"{title} {artist}");

                using var searchReq = new HttpRequestMessage(HttpMethod.Get,
                    $"https://c.y.qq.com/soso/fcgi-bin/client_search_cp?w={keyword}&format=json&p=1&n=1");
                searchReq.Headers.Referrer = new Uri("https://y.qq.com/");
                using var searchResp = await _httpClient.SendAsync(searchReq, token);
                string searchJson = await searchResp.Content.ReadAsStringAsync(token);
                string? songMid = JObject.Parse(searchJson)["data"]?["song"]?["list"]?[0]?["songmid"]?.ToString();
                if (string.IsNullOrEmpty(songMid)) return (null, null);

                using var lyricReq = new HttpRequestMessage(HttpMethod.Get,
                    $"https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?songmid={songMid}&format=json&nobase64=1");
                lyricReq.Headers.Referrer = new Uri("https://y.qq.com/");
                using var lyricResp = await _httpClient.SendAsync(lyricReq, token);
                string lyricJson = await lyricResp.Content.ReadAsStringAsync(token);

                string? lyric = JObject.Parse(lyricJson)["lyric"]?.ToString();
                if (string.IsNullOrEmpty(lyric)) return (null, null);

                // 部分情况下即便 nobase64=1 也仍是 base64，简单探测一下
                if (!lyric.Contains('['))
                {
                    try { lyric = Encoding.UTF8.GetString(Convert.FromBase64String(lyric)); } catch { }
                }
                return (lyric, null);
            }
            catch { return (null, null); }
        }

        // 4) 酷狗音乐（非官方接口）—— 三步：搜索拿 hash -> 搜词库拿 id/accesskey -> 下载
        private async Task<(string? Lrc, string? Translation)> FetchFromKugouAsync(string title, string artist, CancellationToken token)
        {
            try
            {
                string keyword = Uri.EscapeDataString($"{title} - {artist}");

                string searchUrl = $"https://mobilecdn.kugou.com/api/v3/search/song?format=json&keyword={keyword}&page=1&pagesize=1&showtype=1";
                string searchJson = await _httpClient.GetStringAsync(searchUrl, token);
                string? hash = JObject.Parse(searchJson)["data"]?["info"]?[0]?["hash"]?.ToString();
                if (string.IsNullOrEmpty(hash)) return (null, null);

                string krcUrl = $"https://krcs.kugou.com/search?ver=1&man=yes&client=mobi&keyword={keyword}&hash={hash}";
                string krcJson = await _httpClient.GetStringAsync(krcUrl, token);
                var candidate = JObject.Parse(krcJson)["candidates"]?[0];
                string? id = candidate?["id"]?.ToString();
                string? accessKey = candidate?["accesskey"]?.ToString();
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(accessKey)) return (null, null);

                string downloadUrl = $"https://lyrics.kugou.com/download?ver=1&client=pc&id={id}&accesskey={accessKey}&fmt=lrc&charset=utf8";
                string downloadJson = await _httpClient.GetStringAsync(downloadUrl, token);
                string? contentBase64 = JObject.Parse(downloadJson)["content"]?.ToString();
                if (string.IsNullOrEmpty(contentBase64)) return (null, null);

                return (Encoding.UTF8.GetString(Convert.FromBase64String(contentBase64)), null);
            }
            catch { return (null, null); }
        }
    }
}
