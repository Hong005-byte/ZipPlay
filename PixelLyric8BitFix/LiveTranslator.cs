using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 网易云那个翻译源已经基本失效了（搜索接口被网易云自己加密/锁死了，见 LyricsFetcher 里的说明），
    /// 双语歌词想继续有得用，只能自己现场翻译。用的是 Google 翻译网页版自己在用的那个接口
    /// （translate.googleapis.com/translate_a/single），不用注册、不用 key、完全免费——风险类型
    /// 跟这个项目本来就在用的那几个非官方歌词接口是一路的：说崩就崩，崩了就是"这次没有翻译"，
    /// 不影响原文歌词正常显示，不是什么新增的风险类别。
    /// </summary>
    internal static class LiveTranslator
    {
        // 每批最多带这么多行一起发一个请求——单个 q 参数塞太多行，URL 长度可能撞到服务器/代理的
        // 长度限制（中日韩文字编码成 URL 之后体积膨胀得更明显），保守起见分批；长歌会发好几个
        // 请求，并发一起发，不是一批批排队等。
        private const int MaxLinesPerBatch = 25;

        // 每行前面加这个符号再一起发过去，靠这个符号在翻译结果里重新切出"一行对一行"的边界。
        // 光加标记还不够保险：实测过，如果一批里有好几行内容很像（比如副歌重复句），Google 自己的
        // 分段逻辑还是会把相邻的相似行合并成一段返回，标记对不整齐——这种情况靠 TranslateRangeAsync
        // 那边的"拆开重试"兜底，这里只负责单次请求怎么发、怎么判断这次请求本身对不对得上。
        private const char LineMarker = '◆';

        /// <summary>
        /// 整份 LRC 逐行翻译，保留原本每行的时间戳，拼回一份新的 LRC 文本——格式上跟网易云给的
        /// tlyric 完全一样，能直接丢进 SetTranslation 那条老路径，不用改任何显示逻辑。
        /// 已经是中文的歌词、或者翻译失败/网络不通，都返回 null（没有翻译不代表出错，调用方
        /// 该怎么显示原文还怎么显示，不用特殊处理）。
        /// </summary>
        public static async Task<string?> TranslateLrcAsync(string lrcContent, HttpClient httpClient, CancellationToken token)
        {
            var lines = LrcParser.ParseLines(lrcContent);
            if (lines.Count == 0) return null;

            // 先用第一行单独探测一下源语言——单行请求不存在"多行被合并"这个问题，判断最可靠；
            // 判断出来已经是中文的话，后面完全不用再发任何翻译请求
            string? detectedLang = await DetectLanguageAsync(lines[0].Text, httpClient, token);
            if (token.IsCancellationRequested) return null;
            if (!string.IsNullOrEmpty(detectedLang) && detectedLang.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // 按 MaxLinesPerBatch 分批，批次之间并发发出去——之前这里是不管歌多长直接把整首歌的行数
            // 一次性丢给 TranslateRangeAsync，注释说"先分批"但代码根本没分，长歌（60+ 行很常见）第一次
            // 请求就是一个超长 URL，本来该走的"大部分歌走几个正常大小的批次"直接变成了"几乎总是先失败
            // 一次，再靠递归拆半兜底"，多绕了一圈请求、也更容易撞到代理/服务器的 URL 长度限制。
            var translatedTexts = new string?[lines.Count];
            var batchTasks = new List<Task>();
            for (int batchStart = 0; batchStart < lines.Count; batchStart += MaxLinesPerBatch)
            {
                int batchCount = Math.Min(MaxLinesPerBatch, lines.Count - batchStart);
                batchTasks.Add(TranslateRangeAsync(lines, batchStart, batchCount, translatedTexts, httpClient, token));
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
                // 用 TotalMinutes 而不是 Minutes——后者到 60 会折回 0，几十分钟以上的"歌"（比如混音合集）
                // 时间戳会算错；正经 LRC 时间戳也是这么写的，不折算成小时
                sb.Append($"[{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 10:D2}]{text}\n");
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }

        private static async Task<string?> DetectLanguageAsync(string sampleText, HttpClient httpClient, CancellationToken token)
        {
            try
            {
                string url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=zh-CN&dt=t&q="
                    + Uri.EscapeDataString(sampleText);
                string json = await httpClient.GetStringAsync(url, token);
                var root = JArray.Parse(json);
                return root.Count > 2 ? root[2]?.ToString() : null;
            }
            catch
            {
                return null; // 探测失败就当探测不出来，后面照常按"不是中文"处理，正常走翻译流程
            }
        }

        // 递归批量翻译：先按 MaxLinesPerBatch 分批发请求；如果某一批返回的分段数跟预期行数对不上
        // （常见原因是这批里有几行内容太相似/重复——比如副歌重复句——Google 自己的分段逻辑把它们
        // 合并了），就把这一批拆成两半分别再试，实在拆到只剩一行还对不上才真的放弃那一行。
        // 这样只有真正"难分"的少数几行会退化成小请求，其余大部分还是走一次批量请求搞定，
        // 不会因为歌里有几句重复歌词就拖累整首歌全部变成一行一个请求。
        private static async Task TranslateRangeAsync(
            List<(int TimeMs, string Text)> lines, int start, int count,
            string?[] translatedTexts, HttpClient httpClient, CancellationToken token)
        {
            if (count <= 0 || token.IsCancellationRequested) return;

            var result = await TranslateBatchAsync(lines, start, count, httpClient, token);
            if (result != null)
            {
                Array.Copy(result, 0, translatedTexts, start, count);
                return;
            }

            if (count == 1) return; // 单独一行都翻不出来（比如这行内容本身有问题），这一行就放弃

            int firstHalf = count / 2;
            await Task.WhenAll(
                TranslateRangeAsync(lines, start, firstHalf, translatedTexts, httpClient, token),
                TranslateRangeAsync(lines, start + firstHalf, count - firstHalf, translatedTexts, httpClient, token));
        }

        // 单次请求：把这一段行数当一批发出去，成功就返回长度等于 count 的翻译数组，
        // 只要行数对不上（不管是请求本身失败，还是 Google 把相似行合并导致分段数不对），
        // 一律返回 null，交给上面 TranslateRangeAsync 决定要不要拆开重试——这个方法本身不做重试。
        private static async Task<string?[]?> TranslateBatchAsync(
            List<(int TimeMs, string Text)> lines, int start, int count, HttpClient httpClient, CancellationToken token)
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
                string json = await httpClient.GetStringAsync(url, token);
                var root = JArray.Parse(json);

                if (root.Count == 0 || root[0] is not JArray segments) return null;

                var texts = new string?[count];
                int segIndex = 0;
                bool anySegmentMalformed = false;
                foreach (var seg in segments)
                {
                    if (segIndex >= count) break; // 分段数比预期多，多出来的不要，行数对不齐宁可少不要多
                    string? translated = seg is JArray segArr && segArr.Count > 0 ? segArr[0]?.ToString() : null;
                    if (translated != null)
                    {
                        texts[segIndex] = translated.Trim().TrimStart(LineMarker).Trim();
                    }
                    else
                    {
                        // 这一段本身格式不对（比如是空数组）——之前这里只把这一行留 null，segIndex 照样加一，
                        // 只要总段数凑够 count 就当整批成功返回，这一行的翻译就悄悄丢了，TranslateRangeAsync
                        // 那套"拆开重试"完全不会被触发（它只看数量对不对得上，不看内容）。标记一下，让整批
                        // 判定为失败，交给上面拆开重试，这一行才有机会在更小的批次里单独翻出来
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
