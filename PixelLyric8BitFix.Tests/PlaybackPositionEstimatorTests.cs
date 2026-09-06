using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    public class PlaybackPositionEstimatorTests
    {
        private static readonly DateTimeOffset AnchorTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Estimate_Playing_AdvancesByElapsedRealTime()
        {
            var position = PlaybackPositionEstimator.Estimate(
                anchorPosition: TimeSpan.FromSeconds(10),
                anchorTime: AnchorTime,
                now: AnchorTime + TimeSpan.FromSeconds(3),
                isPlaying: true,
                playbackRate: 1.0,
                totalDuration: TimeSpan.FromMinutes(4));

            Assert.Equal(TimeSpan.FromSeconds(13), position);
        }

        [Fact]
        public void Estimate_Paused_StaysAtAnchor()
        {
            // 暂停状态不该继续往前插值——哪怕 now 比 anchorTime 晚了很久，位置应该钉在锚点上不动
            var position = PlaybackPositionEstimator.Estimate(
                anchorPosition: TimeSpan.FromSeconds(10),
                anchorTime: AnchorTime,
                now: AnchorTime + TimeSpan.FromSeconds(30),
                isPlaying: false,
                playbackRate: 1.0,
                totalDuration: TimeSpan.FromMinutes(4));

            Assert.Equal(TimeSpan.FromSeconds(10), position);
        }

        [Fact]
        public void Estimate_DoubleSpeed_AdvancesTwiceAsFast()
        {
            var position = PlaybackPositionEstimator.Estimate(
                anchorPosition: TimeSpan.Zero,
                anchorTime: AnchorTime,
                now: AnchorTime + TimeSpan.FromSeconds(2),
                isPlaying: true,
                playbackRate: 2.0,
                totalDuration: TimeSpan.FromMinutes(4));

            Assert.Equal(TimeSpan.FromSeconds(4), position);
        }

        [Fact]
        public void Estimate_ZeroOrNegativeRate_FallsBackToNormalSpeed()
        {
            // 系统偶尔会汇报一个不合理的倍速（0 或负数）——按 1.0 正常速度处理，不然会算出位置倒退
            var position = PlaybackPositionEstimator.Estimate(
                anchorPosition: TimeSpan.FromSeconds(5),
                anchorTime: AnchorTime,
                now: AnchorTime + TimeSpan.FromSeconds(1),
                isPlaying: true,
                playbackRate: 0,
                totalDuration: TimeSpan.FromMinutes(4));

            Assert.Equal(TimeSpan.FromSeconds(6), position);
        }

        [Fact]
        public void Estimate_ClampsToTotalDuration()
        {
            var position = PlaybackPositionEstimator.Estimate(
                anchorPosition: TimeSpan.FromSeconds(59),
                anchorTime: AnchorTime,
                now: AnchorTime + TimeSpan.FromSeconds(10),
                isPlaying: true,
                playbackRate: 1.0,
                totalDuration: TimeSpan.FromSeconds(60));

            Assert.Equal(TimeSpan.FromSeconds(60), position);
        }

        [Fact]
        public void Estimate_ZeroTotalDuration_DoesNotClamp()
        {
            // 还没拿到真实时长（比如刚切歌那一瞬间）传 TimeSpan.Zero，表示"不知道上限"，不该把结果夹成 0
            var position = PlaybackPositionEstimator.Estimate(
                anchorPosition: TimeSpan.FromMinutes(5),
                anchorTime: AnchorTime,
                now: AnchorTime + TimeSpan.FromSeconds(10),
                isPlaying: true,
                playbackRate: 1.0,
                totalDuration: TimeSpan.Zero);

            Assert.Equal(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(10), position);
        }

        [Fact]
        public void Estimate_NeverGoesNegative()
        {
            var position = PlaybackPositionEstimator.Estimate(
                anchorPosition: TimeSpan.Zero,
                anchorTime: AnchorTime,
                now: AnchorTime - TimeSpan.FromSeconds(5), // now 比锚点还早，理论上不该发生，但不该崩/算出负数
                isPlaying: true,
                playbackRate: 1.0,
                totalDuration: TimeSpan.FromMinutes(4));

            Assert.Equal(TimeSpan.Zero, position);
        }
    }
}
