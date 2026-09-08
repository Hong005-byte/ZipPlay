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
    public sealed record LyricsFetchResult(string Lrc, string? TranslationLrc);

    /// <summary>
    /// 多引擎并发抓词：LRCLIB / 网易云 / QQ音乐 / 酷狗，谁先给出"时长对得上"的结果就用谁。
    /// 从 MainWindow 里搬出来单独成类——纯粹是"给个歌名/歌手/期望时长，要么给我一份能用的 LRC，
    /// 要么给 null"，不碰 UI、不碰缓存，以后改抓词逻辑或者单独测试都不用在主窗口那个大文件里翻。
    /// </summary>
    public sealed class LyricsFetcher
    {
        private readonly HttpClient _httpClient;

        // 部分引擎（尤其是逆向出来的非官方接口，网易云/QQ音乐/酷狗都是）对没带 User-Agent 的请求会
        // 直接拒绝或者限流。桌面版一直没显式带这个头也大多能跑通，大概率是桌面常见的家庭宽带固定/
        // 半固定 IP 本来就不太容易被这类反爬策略盯上；手机走的是运营商 NAT 出口 IP，跟大量其它用户
        // 共用同一个出口 IP，更容易被限流/拒绝——这是手机上"很多歌词都找不到"的一个可能成因，加一个
        // 常见浏览器 UA 不会让情况变得更差，见 LyricsFetcher 构造函数。
        private const string DefaultUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

        public LyricsFetcher(HttpClient httpClient)
        {
            _httpClient = httpClient;

            // 只在调用方还没设置过的时候补一次，不覆盖以后可能出现的自定义值；同一个 HttpClient 实例
            // 也会被 LyricsTranslator 共用（见 FullScreenPlayerPage/FloatingOverlayService 里两个都拿
            // 同一个 _httpClient 构造），这个 UA 对翻译那条请求（Google 翻译网页版接口）同样有意义,
            // 一次设置两边都受益，不用分别设置两遍。
            if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
            {
                _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
            }
        }

        /// <summary>对外的入口——先带着调用方给的 artist 查一遍（见 FetchOnePassAsync），全军覆没
        /// 且 artist 确实非空的话，再用空 artist 兜底重试一遍，见下面那段注释。两遍都落空/被取消
        /// 就返回 null。</summary>
        public async Task<LyricsFetchResult?> FetchAsync(
            string title, string artist, TimeSpan expectedDuration, CancellationToken token,
            Action<string>? onLateTranslation = null)
        {
            var result = await FetchOnePassAsync(title, artist, expectedDuration, token, onLateTranslation);
            if (result != null) return result;

            // 带着 artist 那一遍全军覆没了——手机上不少播放 App（尤其不是专门的音乐 App）报的
            // artist 字段不干净：夹带好几个署名/合作方、频道名、平台 ID 之类的杂质，四个引擎大多是
            // "标题+艺人"整串去搜，artist 这段杂质越多，搜索串跟对方库里干净的标题/艺人越对不上，
            // 越容易全部落空——这是"手机上很多歌词找不到"的一个可能成因，桌面版从本地文件 ID3 标签
            // 读到的 artist 通常干净得多，不太会踩到这个问题。只用标题再搜一遍，大多数引擎支持模糊
            // 搜索，命中率通常比死磕一个不准的 artist 高；只有 artist 本来非空才值得再试，不然就是
            // 同一次查询重复问一遍，白费一次网络往返。
            if (string.IsNullOrWhiteSpace(artist) || token.IsCancellationRequested) return null;
            return await FetchOnePassAsync(title, "", expectedDuration, token, onLateTranslation);
        }

        // 四引擎并发抓词，谁先给出时长对得上的结果就用谁；全部落空（或者版本都对不上）返回 null——
        // FetchAsync 会照着这个结果决定要不要带空 artist 再调一次这同一个方法。
        //
        // 翻译这块单独处理：四个引擎里只有网易云会带翻译，其它三个接口本身不返回翻译。以前的写法是
        // "谁先给出原文就直接返回谁的翻译字段"——如果网易云不是最先响应的那个（大概率不是，
        // LRCLIB/QQ/酷狗随便一个先回来就直接用了），网易云就算随后确实抓到了翻译，也完全没人等它，
        // 白白浪费掉，翻译功能因此经常"根本没出现过"，不是因为歌真的没有翻译。
        // 现在改成：原文该多快出来还是多快出来（不拖慢主流程），但如果赢的不是网易云，
        // 网易云那个请求不取消、放到后台继续等，真等到了（且版本对得上号）再通过 onLateTranslation
        // 回调补一份翻译上去——原文显示速度完全不受影响，只是翻译不再白白被浪费。
        private async Task<LyricsFetchResult?> FetchOnePassAsync(
            string title, string artist, TimeSpan expectedDuration, CancellationToken token,
            Action<string>? onLateTranslation)
        {
            var neteaseTask = FetchFromNeteaseAsync(title, artist, token);
            var pending = new List<Task<(string? Lrc, string? Translation)>>
            {
                FetchFromLrcLibAsync(title, artist, token),
                neteaseTask,
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

                // 赢的不是网易云的话，它的翻译（如果有）单独放到后台继续等，不阻塞这里的返回
                bool neteaseWon = finished == neteaseTask;
                if (!neteaseWon)
                {
                    _ = AwaitLateTranslationAsync(neteaseTask, expectedDuration, token, onLateTranslation);
                }

                return new LyricsFetchResult(lrc, translation);
            }

            return null;
        }

        // 网易云翻译"迟到"了才会走这条路：原文早就用别的引擎抓到并显示了，这里只管把翻译单独补上去。
        // 补之前要重新拿网易云自己那份 LRC 再校验一次时长——网易云抓到的可能是另一个版本（比如对方
        // 库里收录的是加速版），翻译的时间戳是跟它自己那份原文对齐的，用在别的引擎给的原文上时间轴会错位，
        // 版本对不上就宁可不要这份翻译，不能给一份会跑偏的双语显示。
        private static async Task AwaitLateTranslationAsync(
            Task<(string? Lrc, string? Translation)> neteaseTask, TimeSpan expectedDuration,
            CancellationToken token, Action<string>? onLateTranslation)
        {
            if (onLateTranslation == null) return;
            try
            {
                var (lrc, translation) = await neteaseTask;
                if (token.IsCancellationRequested) return;
                if (string.IsNullOrWhiteSpace(translation)) return;
                if (string.IsNullOrEmpty(lrc) || !IsDurationPlausible(lrc, expectedDuration)) return;

                onLateTranslation(translation);
            }
            catch
            {
                // 网易云这条后台请求本身失败/超时都不算事——原文早就用别的引擎显示出来了，
                // 这里只是"锦上添花"的翻译补丁，补不上就算了，不影响正常播放
            }
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
            catch
            {
                return (null, null);
            }
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
