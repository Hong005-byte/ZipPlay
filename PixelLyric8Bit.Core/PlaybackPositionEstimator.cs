namespace PixelLyric8BitFix
{
    /// <summary>
    /// "拿到一个播放位置锚点（某个时刻系统汇报的位置）+ 当前时间，插值算出此刻实际播放到哪了"——
    /// 这段纯数学从桌面版 MainWindow.xaml.cs 的 SmoothTimer_Tick 抄出来单独成一个纯函数：桌面版靠订阅
    /// SMTC 的 TimelinePropertiesChanged/PlaybackInfoChanged 拿到锚点，之后每帧本地插值，不再为了画面
    /// 平滑就去重新问系统"现在到哪了"（那是一次跨进程调用，桌面版当年就是为了省这个开销才改成插值的）。
    ///
    /// Android 端（PixelLyric8Bit.Mobile 的 FloatingOverlayService）用 MediaController.Callback 订阅
    /// 播放状态变化拿到同样意义的锚点，本地渲染 tick 就调这个函数插值，不用每次都问一次
    /// MediaSessionManager——跟桌面版是同一个思路，同一份数学，写一遍两边共用。
    /// </summary>
    public static class PlaybackPositionEstimator
    {
        /// <param name="anchorPosition">锚点时刻系统汇报的播放位置</param>
        /// <param name="anchorTime">拿到这个锚点时的本地时间（不是系统汇报的时间戳，是收到通知那一刻的 Now）</param>
        /// <param name="now">要估算"此刻"播放到哪，传进来的这个"此刻"</param>
        /// <param name="isPlaying">锚点时刻是不是正在播放——暂停/缓冲状态不该继续往前插值，位置钉在锚点上不动</param>
        /// <param name="playbackRate">播放速率（倍速播放场景），&lt;= 0 视为异常值按 1.0 处理，不然可能算出位置倒退</param>
        /// <param name="totalDuration">这首歌总时长，&gt; 0 时用来把结果夹到 [0, totalDuration] 区间；
        /// 传 <see cref="TimeSpan.Zero"/>（还没拿到真实时长，比如刚切歌那一瞬间）表示"不夹上限"</param>
        public static TimeSpan Estimate(
            TimeSpan anchorPosition,
            DateTimeOffset anchorTime,
            DateTimeOffset now,
            bool isPlaying,
            double playbackRate,
            TimeSpan totalDuration)
        {
            TimeSpan position = anchorPosition;
            if (isPlaying)
            {
                double rate = playbackRate > 0 ? playbackRate : 1.0;
                TimeSpan elapsedRealTime = now - anchorTime;
                position += TimeSpan.FromTicks((long)(elapsedRealTime.Ticks * rate));
            }

            if (totalDuration > TimeSpan.Zero && position > totalDuration) position = totalDuration;
            if (position < TimeSpan.Zero) position = TimeSpan.Zero;

            return position;
        }
    }
}
