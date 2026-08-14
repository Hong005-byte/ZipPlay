using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// LRC 文本的解析/取值全集中在这一个地方。以前 <see cref="MainWindow"/>（整行解析出歌词列表）
    /// 和 <see cref="LyricsFetcher"/>（只要最后一句的时间戳，用来校验版本对不对）各自写了一套
    /// 时间戳正则 + 毫秒归一化逻辑，两份几乎一样的代码分居两个文件，改一处容易漏改另一处。
    /// 现在统一到这，两边都调这一份；顺带因为不碰任何 UI/网络，方便单独写单元测试。
    /// </summary>
    internal static class LrcParser
    {
        private static readonly Regex LineRegex = new(@"\[(?<min>\d+):(?<sec>\d+)[\.:](?<ms>\d+)\](?<text>.*)", RegexOptions.Compiled);

        /// <summary>把 LRC 全文解析成按时间排序的 (时间毫秒, 文本) 列表。同一时间戳出现多次时，后出现的覆盖前面的。</summary>
        public static List<(int TimeMs, string Text)> ParseLines(string? lrcContent)
        {
            if (string.IsNullOrEmpty(lrcContent)) return new List<(int, string)>();

            var lines = lrcContent.Replace("\r", "").Split('\n');
            var tempDict = new Dictionary<int, string>();

            foreach (var line in lines)
            {
                var match = LineRegex.Match(line);
                if (!match.Success) continue;

                int totalMs = ParseTimestamp(match);
                string text = match.Groups["text"].Value.Trim();

                if (!string.IsNullOrEmpty(text))
                {
                    tempDict[totalMs] = text;
                }
            }

            return tempDict.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)).ToList();
        }

        /// <summary>拿 LRC 里最后一句歌词的时间戳，粗略估算"这份歌词是多长的版本"，用来跟播放源报告的真实时长交叉校验。</summary>
        public static TimeSpan? GetLastTimestamp(string? lrcContent)
        {
            if (string.IsNullOrEmpty(lrcContent)) return null;

            int maxMs = -1;
            foreach (Match m in LineRegex.Matches(lrcContent))
            {
                int totalMs = ParseTimestamp(m);
                if (totalMs > maxMs) maxMs = totalMs;
            }
            return maxMs >= 0 ? TimeSpan.FromMilliseconds(maxMs) : null;
        }

        private static int ParseTimestamp(Match match)
        {
            int min = int.Parse(match.Groups["min"].Value);
            int sec = int.Parse(match.Groups["sec"].Value);
            string msStr = match.Groups["ms"].Value;

            if (msStr.Length == 1) msStr += "00";
            else if (msStr.Length == 2) msStr += "0";
            int ms = int.Parse(msStr.Substring(0, 3));

            return (min * 60 + sec) * 1000 + ms;
        }
    }
}
