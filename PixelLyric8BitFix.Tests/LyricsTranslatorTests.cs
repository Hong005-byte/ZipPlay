using System.Net;
using System.Net.Http;
using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    public class LyricsTranslatorTests
    {
        // 假的 HttpMessageHandler——用请求 URL 里的 q 参数判断这次是"探测语言"（单行、没有 LineMarker）
        // 还是"批量翻译"（带 ◆ 标记），分别返回预先准备好的 Google 翻译接口格式 JSON，不用真的联网，
        // 测试跑起来又快又不受网络状况影响。
        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<bool, int> _callCounter;
            private readonly Func<bool /*isBatch*/, string /*decodedQuery*/, string /*jsonResponse*/> _respond;

            public int CallCount { get; private set; }

            public StubHandler(Func<bool, string, string> respond)
            {
                _respond = respond;
                _callCounter = _ => ++CallCount;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string url = request.RequestUri!.ToString();
                int qIndex = url.IndexOf("q=", StringComparison.Ordinal);
                string decodedQuery = Uri.UnescapeDataString(url[(qIndex + 2)..]);
                bool isBatch = decodedQuery.Contains('◆');
                _callCounter(isBatch);

                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_respond(isBatch, decodedQuery)),
                };
                return Task.FromResult(response);
            }
        }

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => throw new HttpRequestException("模拟网络故障");
        }

        [Fact]
        public async Task TranslateLrcAsync_EmptyLrc_ReturnsNullWithoutAnyRequest()
        {
            var handler = new StubHandler((_, _) => throw new InvalidOperationException("不该发出任何请求"));
            var translator = new LyricsTranslator(new HttpClient(handler));

            var result = await translator.TranslateLrcAsync("", CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task TranslateLrcAsync_DetectedChinese_ReturnsNullAndSkipsBatchTranslation()
        {
            var handler = new StubHandler((isBatch, _) =>
            {
                Assert.False(isBatch, "识别出已经是中文，不该再发批量翻译请求");
                return """[[["某句","某句",null,null,0]],null,"zh-CN"]""";
            });
            var translator = new LyricsTranslator(new HttpClient(handler));

            var result = await translator.TranslateLrcAsync("[00:00.00]某句歌词", CancellationToken.None);

            Assert.Null(result);
            Assert.Equal(1, handler.CallCount); // 只发了探测语言那一次请求
        }

        [Fact]
        public async Task TranslateLrcAsync_EnglishSource_TranslatesEachLineWithOriginalTimestamps()
        {
            var handler = new StubHandler((isBatch, _) => isBatch
                ? """[[["你好","Hello",null,null,0],["世界","World",null,null,0]],null,"en"]"""
                : """[[["你好","Hello",null,null,0]],null,"en"]""");
            var translator = new LyricsTranslator(new HttpClient(handler));

            var result = await translator.TranslateLrcAsync("[00:00.00]Hello\n[00:03.00]World", CancellationToken.None);

            Assert.Equal("[00:00.00]你好\n[00:03.00]世界\n", result);
        }

        [Fact]
        public async Task TranslateLrcAsync_AllRequestsFail_ReturnsNullWithoutThrowing()
        {
            var translator = new LyricsTranslator(new HttpClient(new ThrowingHandler()));

            var result = await translator.TranslateLrcAsync("[00:00.00]Hello", CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task TranslateLrcAsync_SegmentCountMismatch_FallsBackToPerLineRetry()
        {
            // 批量请求第一次返回的分段数（1 段）跟预期行数（2 行）对不上——常见原因是 Google 把相似行合并了，
            // 应该拆成两个单行请求各自重试，而不是直接放弃整批
            var handler = new StubHandler((isBatch, query) =>
            {
                if (!isBatch) return """[[["你好","Hello",null,null,0]],null,"en"]""";

                // 拆开重试之后，单行请求的 q 只会带一行——用这个区分"第一次没拆开的整批请求"
                // 和"拆开之后的单行请求"
                bool isSingleLineRetry = query.Trim('\n').Split('\n').Length == 1;
                if (!isSingleLineRetry)
                {
                    return """[[["合并的一段",null,null,null,0]],null,"en"]"""; // 故意只给 1 段，制造对不上的情况
                }

                return query.Contains("Hello")
                    ? """[[["你好",null,null,null,0]],null,"en"]"""
                    : """[[["世界",null,null,null,0]],null,"en"]""";
            });
            var translator = new LyricsTranslator(new HttpClient(handler));

            var result = await translator.TranslateLrcAsync("[00:00.00]Hello\n[00:03.00]World", CancellationToken.None);

            Assert.Equal("[00:00.00]你好\n[00:03.00]世界\n", result);
        }
    }
}
