using System;
using System.Text;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 主题"分享码"——把整份主题 JSON 压成一段带识别前缀的 Base64 纯文本，能直接粘贴到聊天框/群消息里，
    /// 不用像"导出成文件"那样先落盘、再发文件、对方还得找地方存。前缀 "ZPT1:" 只是给 TryDecode 一个
    /// 快速判断"这段文本看起来像不像分享码"的依据，不是加密也不是校验——分享码本身一样能被手滑改坏，
    /// 真正的合法性还是靠 TryDecode 成功之后再走一遍 CustomThemeValidator.ParseAndValidate 才能确定，
    /// 这个类只管"文本 ⇄ JSON"这一步转换，不重复校验逻辑。
    /// </summary>
    public static class CustomThemeShareCode
    {
        private const string Prefix = "ZPT1:";

        public static string Encode(string rawJson) => Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(rawJson));

        /// <summary>解不出来（没有前缀、Base64 损坏、损坏后不是合法 UTF-8）统一返回 false，
        /// 调用方应该提示"没找到有效的分享码"，不用区分具体是哪种坏法——用户也分不清，说了也没用。</summary>
        public static bool TryDecode(string code, out string json)
        {
            json = "";
            string trimmed = code.Trim();
            if (!trimmed.StartsWith(Prefix, StringComparison.Ordinal)) return false;

            try
            {
                json = Encoding.UTF8.GetString(Convert.FromBase64String(trimmed[Prefix.Length..]));
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
