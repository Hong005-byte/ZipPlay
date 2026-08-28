using System;
using System.Linq;
using System.Windows;

namespace PixelLyric8BitFix
{
    // 听歌统计：记录每天听了多久、听了哪首歌，纯本地、不联网，用来生成月度/年度总结报告
    // （见 ListeningStatsWindow）。字段声明在 MainWindow.xaml.cs（沿用这个类"字段都归 xaml.cs"的惯例），
    // 这里只放行为。
    //
    // 攒时长的思路：SmoothTimer_Tick 每 50ms 调一次 UpdateListeningStats，按真实墙钟流逝时间
    // （不是按 _playbackRate 放大）累加进内存里的 _pendingListenSeconds，不是每个 tick 都写盘——
    // 攒够 StatsFlushThresholdSeconds 才 flush 进 _stats 并存一次盘，避免 50ms 一次的热路径上做磁盘 I/O。
    // 另外两个 flush 时机：切歌那一刻（HandleTrackChangeAsync 里，把上一首歌剩下的零头记进去）、
    // 窗口关闭那一刻（Closed 事件里，不丢最后几秒）。
    public partial class MainWindow : Window
    {
        private const double StatsFlushThresholdSeconds = 10;

        // 单次 tick 之间隔了太久（电脑睡眠/休眠恢复、调试器断点之类）的话，这次的 elapsed 会异常地大，
        // 直接按这么长时间计入听歌时长明显不合理——超过这个上限就当"这段间隔不算数"，不计入统计，
        // 也不会因为这一下把 _lastStatsTickTime 拉飞导致后面持续算错
        private const double StatsMaxTickGapSeconds = 3;

        private void UpdateListeningStats()
        {
            var now = DateTimeOffset.Now;
            double elapsed = (now - _lastStatsTickTime).TotalSeconds;
            _lastStatsTickTime = now;

            if (_isPlaying && !string.IsNullOrEmpty(_lastTrackId) && elapsed > 0 && elapsed < StatsMaxTickGapSeconds)
            {
                _pendingListenSeconds += elapsed;

                // 客制化主题 icon.actions 的数据驱动自动切换：这首歌连续播放了多久喂给
                // EvaluateAutoSwitchIconAction（MainWindow.Skins.cs）——没有任何动作用到
                // autoSwitchAfterSeconds 的话这一步什么都不做，纯粹多比较几个 double，可以忽略的开销
                _customIconContinuousTrackSeconds += elapsed;
                EvaluateAutoSwitchIconAction();
            }

            if (_pendingListenSeconds >= StatsFlushThresholdSeconds)
            {
                FlushListeningStats(_lastTrackId);
            }
        }

        // 把 _pendingListenSeconds 记到 trackId 名下的"今天"这一桶里，然后存盘。trackId 传的是"这段
        // 时长归属哪首歌"——大多数情况下就是当前 _lastTrackId，但切歌瞬间要传*切歌前*的那个 trackId
        // （见 HandleTrackChangeAsync），不然最后几秒会被记到刚切过去的新歌头上
        private void FlushListeningStats(string? trackId)
        {
            if (_pendingListenSeconds < 1 || string.IsNullOrEmpty(trackId))
            {
                _pendingListenSeconds = 0;
                return;
            }

            int seconds = (int)Math.Round(_pendingListenSeconds);
            _pendingListenSeconds = 0;
            if (seconds <= 0) return;

            string dayKey = DateTime.Now.ToString("yyyy-MM-dd");
            if (!_stats.Days.TryGetValue(dayKey, out var day))
            {
                day = new DayStats();
                _stats.Days[dayKey] = day;
            }
            day.TotalSeconds += seconds;
            day.TrackSeconds[trackId] = day.TrackSeconds.GetValueOrDefault(trackId) + seconds;

            ListeningStatsStore.Save(_stats);
            CelebrateNewlyUnlockedAchievements();
        }

        // 每次真的写了新的听歌数据（唯一会让听歌成就状态发生变化的时机）之后，顺手评估一遍 8 个听歌成就，
        // 把"这次相比上次多解锁的"弹成 toast——AchievementCalculator 本身不碰磁盘、没有独立的已解锁状态，
        // 靠 AchievementUnlockTracker 记一份"见过哪些"才知道"这个是不是刚刚才解锁"，不是每次都当新的庆祝一遍。
        // 挂在 FlushListeningStats 末尾而不是每个 50ms tick 里，是因为只有这里才真的改了 _stats 的内容，
        // 别的 tick 只是攒 _pendingListenSeconds，成就状态不可能变，没必要每 50ms 重新评估一次。
        private void CelebrateNewlyUnlockedAchievements()
        {
            var progress = AchievementCalculator.Evaluate(_stats);
            var newlyUnlocked = AchievementUnlockTracker.DetectNewlyUnlocked(progress);
            if (newlyUnlocked.Count == 0) return;

            // ShowToast 只有一块地方显示，不是消息队列——同一次 flush 就凑齐两个成就的情况是真会发生的
            // （比如刚好这次听歌时长跨过 100 小时那一刻，7 个常规成就已经全亮了，压轴的"尊贵听众"也跟着
            // 同时解锁），分开调用两次 ShowToast 后一条会立刻覆盖前一条，前一条等于白弹。多个一起解锁的话
            // 拼进同一条 toast 里，不会有哪个被静默吃掉。
            string names = string.Join("、", newlyUnlocked.Select(a => $"{a.Icon} {a.Name}"));
            ShowToast($"🎉 成就解锁：{names}");
        }

        // HandleTrackChangeAsync 每次真正换了一首新歌就调一次，把这首歌"干净"的标题/艺人（已经去掉
        // remix/feat 标注、多艺人取第一个）记进 Tracks 表——报告页面显示用这份，trackId 本身仍然是
        // 原始未清洗的 "标题_艺人"，两者分开存，见 ListeningStats.cs 顶部注释
        private void RegisterTrackInfo(string trackId, string title, string artist)
        {
            _stats.Tracks[trackId] = new TrackInfo { Title = title, Artist = artist };
        }
    }
}
