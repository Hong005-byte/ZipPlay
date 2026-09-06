using System.Text;
using System.Text.Json;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 双语歌词的翻译源——逻辑照抄桌面版 PixelLyric8BitFix/LiveTranslator.cs（同一套分批 + 递归拆分
    /// 兜底策略，同一个 Google 翻译网页版接口），改了三处纯"平台专属"的东西，不是算法本身有变化：
    /// 1. HttpClient 构造函数传进来，不是内部自己 new 一个——跟 LyricsFetcher 同一个模式，桌面版那份
    ///    专用 HttpClient 是靠 NetworkHelpers 强制走 IPv4（WPF 项目专属，见桌面版类注释），Core 这边
    ///    不该假设调用方一定有这个坑要绕，创建 HttpClient 这件事交还给调用方
    /// 2. 不调 AppLog——那是纯 WPF 项目里的东西，Core 里其它搬过来的纯逻辑（LyricsFetcher 等）也是
    ///    同样处理：失败就静默降级，不记日志，这不是新开的先例
    /// 3. 类名改成 LyricsTranslator 不叫 LiveTranslator——两边都在 PixelLyric8BitFix 这个命名空间下，
    ///    桌面项目又直接引用 Core，撞名会导致桌面那边"到底用哪个 LiveTranslator"产生二义性编译错误，
    ///    见 LrcParser.cs 同一命名空间下的其它文件都没有这个问题、只有这个类目前撞了
    ///
    /// 用的是 Google 翻译网页版自己在用的那个接口（translate.googleapis.com/translate_a/single），
    /// 不用注册/key，免费——跟这个项目本来就在用的那几个非官方歌词接口是一路的风险：说崩就崩，崩了
    /// 就是"这次没有翻译"，不影响原文歌词正常显示。
    /// </summary>
    public sealed class LyricsTranslator
    {
        private const int MaxLinesPerBatch = 25; // 单个 q 参数塞太多行会撞 URL 长度限制，分批发、并发一起发
        private const char LineMarker = '◆'; // 每行前面加这个符号，靠它在翻译结果里重新切出"一行对一行"的边界

        private readonly HttpClient _httpClient;

        public LyricsTranslator(HttpClient httpClient) => _httpClient = httpClient;

        /// <summary>整份 LRC 逐行翻译，保留原本每行的时间戳，拼回一份新的 LRC 文本。已经是中文的歌词、
        /// 或者翻译失败/网络不通，都返回 null——没有翻译不代表出错，调用方该怎么显示原文还怎么显示。</summary>
        public async Task<string?> TranslateLrcAsync(string lrcContent, CancellationToken token)
        {
            var lines = LrcParser.ParseLines(lrcContent);
            if (lines.Count == 0) return null;

            // 先用第一行单独探测源语言——单行请求不存在"多行被合并"这个问题，判断最可靠
            string? detectedLang = await DetectLanguageAsync(lines[0].Text, token);
            if (token.IsCancellationRequested) return null;
            if (!string.IsNullOrEmpty(detectedLang) && detectedLang.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                return null; // 原文本来就是中文，没有翻译可翻，这不算失败
            }

            var translatedTexts = new string?[lines.Count];
            var batchTasks = new List<Task>();
            for (int batchStart = 0; batchStart < lines.Count; batchStart += MaxLinesPerBatch)
            {
                int batchCount = Math.Min(MaxLinesPerBatch, lines.Count - batchStart);
                batchTasks.Add(TranslateRangeAsync(lines, batchStart, batchCount, translatedTexts, token));
            }
            await Task.WhenAll(batchTasks);

            if (token.IsCancellationRequested) return null;
            if (!translatedTexts.Any(t => !string.IsNullOrEmpty(t))) return null;

            var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                string? text = translatedTexts[i];
                if (string.IsNullOrEmpty(text)) continue; // 这一行翻译失败就跳过，不强行拼一条空行进去

                var ts = TimeSpan.FromMilliseconds(lines[i].TimeMs);
                // 用 TotalMinutes 而不是 Minutes——后者到 60 会折回 0，几十分钟以上的"歌"时间戳会算错
                sb.Append($"[{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 10:D2}]{text}\n");
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }

        private async Task<string?> DetectLanguageAsync(string sampleText, CancellationToken token)
        {
            try
            {
                string url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=zh-CN&dt=t&q="
                    + Uri.EscapeDataString(sampleText);
                string json = await _httpClient.GetStringAsync(url, token);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                return root.GetArrayLength() > 2 ? root[2].GetString() : null;
            }
            catch
            {
                return null; // 探测失败就当探测不出来，后面照常按"不是中文"处理，不影响功能
            }
        }

        // 递归批量翻译：先按 MaxLinesPerBatch 分批发请求；如果某一批返回的分段数跟预期行数对不上
        // （常见原因是这批里有几行内容太相似/重复，Google 自己的分段逻辑把它们合并了），就把这一批
        // 拆成两半分别再试，实在拆到只剩一行还对不上才真的放弃那一行
        private async Task TranslateRangeAsync(
            List<(int TimeMs, string Text)> lines, int start, int count,
            string?[] translatedTexts, CancellationToken token)
        {
            if (count <= 0 || token.IsCancellationRequested) return;

            var result = await TranslateBatchAsync(lines, start, count, token);
            if (result != null)
            {
                Array.Copy(result, 0, translatedTexts, start, count);
                return;
            }

            if (count == 1) return; // 单独一行都翻不出来（比如这行内容本身有问题），这一行就放弃

            int firstHalf = count / 2;
            await Task.WhenAll(
                TranslateRangeAsync(lines, start, firstHalf, translatedTexts, token),
                TranslateRangeAsync(lines, start + firstHalf, count - firstHalf, translatedTexts, token));
        }

        // 单次请求：把这一段行数当一批发出去，成功就返回长度等于 count 的翻译数组，只要行数对不上
        // （不管是请求本身失败，还是 Google 把相似行合并导致分段数不对），一律返回 null，交给上面
        // TranslateRangeAsync 决定要不要拆开重试
        private async Task<string?[]?> TranslateBatchAsync(
            List<(int TimeMs, string Text)> lines, int start, int count, CancellationToken token)
        {
            try
            {
                var sb = new StringBuilder();
                for (int i = 0; i < count; i++)
                {
                    sb.Append(LineMarker).Append(lines[start + i].Text).Append('\n');
                }

                string url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=zh-CN&dt=t&q="
                    + Uri.EscapeDataString(sb.ToString());
                string json = await _httpClient.GetStringAsync(url, token);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.GetArrayLength() == 0 || root[0].ValueKind != JsonValueKind.Array) return null;
                var segments = root[0];

                var texts = new string?[count];
                int segIndex = 0;
                bool anySegmentMalformed = false;
                foreach (var seg in segments.EnumerateArray())
                {
                    if (segIndex >= count) break; // 分段数比预期多，多出来的不要，行数对不齐宁可少不要多
                    string? translated = seg.ValueKind == JsonValueKind.Array && seg.GetArrayLength() > 0
                        ? seg[0].GetString()
                        : null;
                    if (translated != null)
                    {
                        texts[segIndex] = translated.Trim().TrimStart(LineMarker).Trim();
                    }
                    else
                    {
                        // 这一段本身格式不对——标记一下让整批判定为失败，交给上面拆开重试，这一行才有机会
                        // 在更小的批次里单独翻出来，不然会悄悄丢了这一行的翻译
                        anySegmentMalformed = true;
                    }
                    segIndex++;
                }

                return segIndex == count && !anySegmentMalformed ? texts : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
