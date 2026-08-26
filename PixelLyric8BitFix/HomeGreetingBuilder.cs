using System;
using System.Collections.Generic;
using System.Linq;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 首页问候语——从"固定一句模板"换成"看时间段 + 偶尔看听歌数据"的文案池，每次打开首页都可能不一样，
    /// 不是为了信息量，纯粹是让"打开首页"这件事本身有点小惊喜感。纯函数，不碰磁盘/UI，随机源由调用方
    /// 传进来（HomeWindow 用真随机，测试用固定 seed 让结果可预测）。
    /// </summary>
    internal static class HomeGreetingBuilder
    {
        // 5 个时间段，每段几句可轮换的文案——{0} 是用户名占位符。凌晨那几句故意带点"熬夜"的调侃，
        // 跟其它时段区分开，不是随便凑数。每段多备几句纯粹是为了"多打开几次首页也不容易看腻"，
        // 不是每句都要有多深的含义。
        private static readonly (int StartHour, string[] Phrases)[] TimeBuckets =
        {
            (0, new[]
            {
                "夜猫子模式开启，{0}",
                "这么晚还在听歌呀，{0}",
                "凌晨好，{0}",
                "夜深了，别忘了休息，{0}",
                "熬夜听歌人本人，{0}",
                "月亮和音乐都陪着你，{0}",
            }),
            (5, new[]
            {
                "早上好，{0}",
                "新的一天从一首歌开始，{0}",
                "早安，{0}",
                "早起的鸟儿有音乐听，{0}",
                "元气满满的一天，{0}",
                "喝杯咖啡，配首歌，{0}",
            }),
            (11, new[]
            {
                "中午好，{0}",
                "该听首歌放松一下了，{0}",
                "午休时间，来点音乐吧，{0}",
                "吃饭配歌，{0}",
            }),
            (14, new[]
            {
                "下午好，{0}",
                "摸鱼时间到，{0}",
                "下午茶配一首歌，{0}",
                "困意来袭？来首歌提提神，{0}",
                "阳光正好，音乐也正好，{0}",
            }),
            (18, new[]
            {
                "晚上好，{0}",
                "夜幕降临，放首歌吧，{0}",
                "晚安前听首歌，{0}",
                "忙碌一天，放松一下，{0}",
                "夜晚配音乐更有氛围，{0}",
                "今晚也要好好听歌，{0}",
            }),
        };

        private const string Suffix = " 👋（点这里改资料）";

        /// <summary>没填过名字的话固定走这句引导文案，不套用下面那套时间段/数据文案池——
        /// 这是个引导动作，不该被"随机抽到一句无关的问候"顶掉。</summary>
        public static string Build(DateTime now, string? userName, ListeningStats stats, Random rng)
        {
            if (string.IsNullOrWhiteSpace(userName)) return "设置你的名字/头像 →";

            // 3 成概率，如果听歌数据本身有值得一提的内容，优先挑一句数据相关的问候；
            // 没有够格的数据（比如刚装上、什么都还没听过）就落回时间段文案池
            if (rng.Next(10) < 3)
            {
                string? statsLine = TryBuildStatsGreeting(now, stats, userName, rng);
                if (statsLine != null) return statsLine;
            }

            return BuildTimeGreeting(now.Hour, userName, rng);
        }

        private static string BuildTimeGreeting(int hour, string userName, Random rng)
        {
            // TimeBuckets 按 StartHour 升序排好的，找最后一个 StartHour <= hour 的桶；
            // 小时超出所有桶起点（比如 23 点）自然落进最后一个桶（18 点那档），不用额外处理"跨天"
            var bucket = TimeBuckets.Last(b => hour >= b.StartHour);
            string template = bucket.Phrases[rng.Next(bucket.Phrases.Length)];
            return string.Format(template, userName) + Suffix;
        }

        // 几种"数据够格"的问候话术，每种都有自己的门槛（不是随便什么数字都拿出来说）；
        // 把够格的候选项收集起来再随机挑一个，不是按固定优先级——不然常年是同一句最先满足条件的话术。
        // 8 种候选比一开始的 4 种覆盖的数据维度更全（今天/全部时间、时长/天数/艺人/歌曲/成就都摸到了一点），
        // 抽到""说人话""的问候的概率也更高，不会老是同一两句翻来覆去。
        private static string? TryBuildStatsGreeting(DateTime now, ListeningStats stats, string userName, Random rng)
        {
            var candidates = new List<string>();
            var today = DateOnly.FromDateTime(now);

            int longestStreak = ListeningStatsAggregator.GetLongestStreakDays(stats);
            if (longestStreak >= 3)
            {
                candidates.Add($"你已经连续听了 {longestStreak} 天啦，{userName}{Suffix}");
            }

            int totalSeconds = ListeningStatsAggregator.GetTotalSeconds(stats, DateOnly.MinValue, DateOnly.MaxValue);
            if (totalSeconds >= 3600)
            {
                candidates.Add($"跟你一起听过 {ListeningStatsAggregator.FormatDuration(totalSeconds)} 的歌了，{userName}{Suffix}");
            }

            var topArtists = ListeningStatsAggregator.GetTopArtists(stats, DateOnly.MinValue, DateOnly.MaxValue, topN: 1);
            if (topArtists.Count > 0)
            {
                candidates.Add($"最近好像很喜欢 {topArtists[0].Artist}，{userName}{Suffix}");
            }

            int uniqueArtists = ListeningStatsAggregator.GetUniqueArtistCount(stats);
            if (uniqueArtists >= 10)
            {
                candidates.Add($"已经听过 {uniqueArtists} 位不同的艺人了，{userName}{Suffix}");
            }

            // 今天已经听了多久——用的是"今天"这一天单独的范围，跟上面"全部时间"那几句是不同维度，
            // 门槛设得很低（只要有听）是因为这句本来就是想在"今天正好在用"的时候多冒出来
            int todaySeconds = ListeningStatsAggregator.GetTotalSeconds(stats, today, today);
            if (todaySeconds >= 300) // 5 分钟以上才提，太短的话这句话没什么存在感
            {
                candidates.Add($"今天已经听了 {ListeningStatsAggregator.FormatDuration(todaySeconds)} 了，{userName}{Suffix}");
            }

            var topTracks = ListeningStatsAggregator.GetTopTracks(stats, DateOnly.MinValue, DateOnly.MaxValue, topN: 1);
            if (topTracks.Count > 0)
            {
                candidates.Add($"最近循环最多的是《{topTracks[0].Title}》，{userName}{Suffix}");
            }

            int unlockedAchievements = AchievementCalculator.Evaluate(stats).Count(a => a.Unlocked);
            if (unlockedAchievements > 0)
            {
                candidates.Add($"已经解锁 {unlockedAchievements} 个成就啦，{userName}{Suffix}");
            }

            int activeDays = ListeningStatsAggregator.GetActiveDayCount(stats, DateOnly.MinValue, DateOnly.MaxValue);
            if (activeDays >= 30)
            {
                candidates.Add($"已经用 ZipPlay 听歌 {activeDays} 天了，{userName}{Suffix}");
            }

            return candidates.Count > 0 ? candidates[rng.Next(candidates.Count)] : null;
        }
    }
}
