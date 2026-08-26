using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    /// <summary>"🔗 粘贴分享码导入" / "复制分享码"背后的 CustomThemeShareCode——纯字符串转换，
    /// 不碰磁盘/剪贴板，方便直接写单元测试盯住"编码之后解码能不能拿回原样的 JSON"这个核心行为。</summary>
    public class CustomThemeShareCodeTests
    {
        [Fact]
        public void Encode_ThenDecode_RoundTripsOriginalJson()
        {
            string original = "{\"name\":\"测试主题\",\"font\":\"Segoe UI\"}"; // 特意带中文，验证不是简单 ASCII 也能原样往返
            string code = CustomThemeShareCode.Encode(original);

            Assert.True(CustomThemeShareCode.TryDecode(code, out string decoded));
            Assert.Equal(original, decoded);
        }

        [Fact]
        public void Encode_AlwaysStartsWithRecognizablePrefix()
        {
            // 前缀是 TryDecode 判断"这段文本像不像分享码"的依据，编码出来的结果必须带着它
            string code = CustomThemeShareCode.Encode("{}");
            Assert.StartsWith("ZPT1:", code);
        }

        [Theory]
        [InlineData("")]
        [InlineData("不是分享码的普通文字")]
        [InlineData("ZPT1:这不是合法的Base64###")]
        public void TryDecode_RejectsNonShareCodeText(string input)
        {
            Assert.False(CustomThemeShareCode.TryDecode(input, out _));
        }

        [Fact]
        public void TryDecode_TrimsSurroundingWhitespace()
        {
            // 用户从聊天框粘贴过来的分享码前后经常带换行/空格，不该因为这个就解不出来
            string code = CustomThemeShareCode.Encode("{\"name\":\"x\"}");
            Assert.True(CustomThemeShareCode.TryDecode($"  \n{code}\n  ", out string decoded));
            Assert.Equal("{\"name\":\"x\"}", decoded);
        }
    }
}
