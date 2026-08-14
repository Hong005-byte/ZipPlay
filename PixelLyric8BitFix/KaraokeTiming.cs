using System;
using System.Linq;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 卡拉OK扫光效果"唱到第几个字"的估算逻辑。没有逐字时间戳（免费歌词源基本都只给整行时间），
    /// 所以按估算进度算现在唱到第几个字，不是精确逐字同步，但比整行一次性切换有动感得多。
    /// 从 MainWindow 里搬出来单独成类——这套带经验参数（每字多少毫秒、超时倍数）的估算逻辑
    /// 最容易在下次调参时悄悄跑偏，独立出来才方便写单元测试钉住行为。
    /// </summary>
    internal static class KaraokeTiming
    {
        // 大部分歌词行，"这一句到下一句"的区间本来就大致等于唱这句话要用的时间，直接线性铺开
        // 速度就是自然的。只有少数"唱得很快 + 后面跟一大段前奏间奏"的极端情况，区间会明显
        // 比正常语速该花的时间长很多，这种才需要压缩：按字数估算一个"大概唱这句要多久"，
        // 唱完就停在"整句已点亮"，不会显得扫光在傻等间奏、相对实际人声"跟不上"。
        public const double LatinMsPerChar = 130;  // 经验值，纯英文/拉丁字母歌词的中等语速
        public const double CjkMsPerChar = 300;    // 经验值，纯中文歌词——一个字一个完整音节，天然要唱更久
        public const double OvertimeThreshold = 1.8; // 区间超过估算值的这个倍数，才当成"有间奏"启用压缩

        /// <summary>
        /// 算出当前这一句歌词唱到第几个字了（0 到 lineText.Length 之间）。
        /// </summary>
        /// <param name="lineText">当前行文本（已经 trim 过）。</param>
        /// <param name="currentMs">当前播放位置（含用户手动歌词偏移）。</param>
        /// <param name="lineStartMs">这一行的时间戳。</param>
        /// <param name="lineEndMs">下一行的时间戳（最后一行没有下一行时，调用方传一个兜底值，比如 lineStartMs + 4000）。</param>
        public static int EstimateSungChars(string lineText, int currentMs, int lineStartMs, int lineEndMs)
        {
            if (string.IsNullOrEmpty(lineText)) return 0;

            double progress = EstimateProgress(lineText, currentMs, lineStartMs, lineEndMs);
            return Math.Clamp((int)Math.Round(lineText.Length * progress), 0, lineText.Length);
        }

        /// <summary>同上，但返回 0~1 的进度比例，方便单独测试压缩逻辑本身而不用再反算字数。</summary>
        public static double EstimateProgress(string lineText, int currentMs, int lineStartMs, int lineEndMs)
        {
            int fillDurationMs = EstimateFillDurationMs(lineText, lineStartMs, lineEndMs);
            double progress = (double)(currentMs - lineStartMs) / fillDurationMs;
            return Math.Clamp(progress, 0, 1);
        }

        /// <summary>估算这一行实际"唱完"要花多久（毫秒），已经跟区间长度取过 min。</summary>
        public static int EstimateFillDurationMs(string lineText, int lineStartMs, int lineEndMs)
        {
            int intervalMs = Math.Max(lineEndMs - lineStartMs, 1);
            double msPerChar = EstimateMsPerChar(lineText);
            int estimatedSingMs = Math.Max(300, (int)(lineText.Length * msPerChar));
            return intervalMs > estimatedSingMs * OvertimeThreshold ? estimatedSingMs : intervalMs;
        }

        // 中文字跟英文字母不是一回事：一个中文字基本就是一个完整音节，唱起来比一个英文字母
        // 要慢得多（英文很多字母拼一个音节）。同一个"每字多少毫秒"的基准硬套中文歌，
        // 估算出来的时长会明显偏短，扫光就会显得比中文歌实际唱的快。按这一行里中文字符
        // 的占比，在"纯英文基准"和"纯中文基准"之间插值，混合中英文的行也能按比例算。
        private static double EstimateMsPerChar(string lineText)
        {
            if (lineText.Length == 0) return LatinMsPerChar;
            int cjkCount = lineText.Count(IsCjk);
            double cjkRatio = (double)cjkCount / lineText.Length;
            return LatinMsPerChar + (CjkMsPerChar - LatinMsPerChar) * cjkRatio;
        }

        // CJK 统一表意文字区段（U+4E00 ~ U+9FFF）
        private static bool IsCjk(char c) => c >= '一' && c <= '鿿';
    }
}
