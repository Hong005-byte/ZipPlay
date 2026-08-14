using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    public class AudioVisualizerMathTests
    {
        [Fact]
        public void CompressMagnitude_ZeroMagnitude_ReturnsZero()
        {
            Assert.Equal(0f, AudioVisualizerMath.CompressMagnitude(0, gain: 40, referenceMagnitude: 12.0));
        }

        [Fact]
        public void CompressMagnitude_AtReferenceMagnitude_ReturnsOne()
        {
            // 幅度正好等于"参考满量程"时，压缩结果按定义应该正好是 1（上限）
            float result = AudioVisualizerMath.CompressMagnitude(12.0, gain: 40, referenceMagnitude: 12.0);
            Assert.Equal(1f, result, precision: 5);
        }

        [Fact]
        public void CompressMagnitude_NeverExceedsOne_EvenWithHugeMagnitude()
        {
            float result = AudioVisualizerMath.CompressMagnitude(999999, gain: 40, referenceMagnitude: 12.0);
            Assert.True(result <= 1f);
        }

        [Fact]
        public void CompressMagnitude_IsMonotonicallyIncreasing()
        {
            float low = AudioVisualizerMath.CompressMagnitude(1, gain: 40, referenceMagnitude: 12.0);
            float mid = AudioVisualizerMath.CompressMagnitude(5, gain: 40, referenceMagnitude: 12.0);
            float high = AudioVisualizerMath.CompressMagnitude(10, gain: 40, referenceMagnitude: 12.0);
            Assert.True(low < mid);
            Assert.True(mid < high);
        }

        [Fact]
        public void AverageRange_ComputesSimpleAverage()
        {
            float[] levels = { 0f, 0.2f, 0.4f, 0.6f, 0.8f, 1f };
            // [1, 4) -> 0.2, 0.4, 0.6 的平均
            float avg = AudioVisualizerMath.AverageRange(levels, 1, 4);
            Assert.Equal(0.4f, avg, precision: 5);
        }

        [Fact]
        public void AverageRange_EndBeyondArrayLength_ClampsToArrayLength()
        {
            float[] levels = { 0.5f, 0.5f };
            float avg = AudioVisualizerMath.AverageRange(levels, 0, 100);
            Assert.Equal(0.5f, avg, precision: 5);
        }

        [Fact]
        public void AverageRange_EmptyRange_ReturnsZero_NotException()
        {
            float[] levels = { 0.5f, 0.5f };
            Assert.Equal(0f, AudioVisualizerMath.AverageRange(levels, 5, 5));
        }

        [Fact]
        public void ComputeImpact_InstantBelowMinFloor_NeverTriggers_EvenWithZeroBaseline()
        {
            // baseline 是 0 的时候 triggerLevel 也是 0，几乎什么都能"冲过" triggerLevel，
            // 但 minFloor 这道绝对下限得先过——安静段落的正常波动不该被当成一次冲击
            float baseline = 0f;
            float impact = AudioVisualizerMath.ComputeImpact(0.03f, ref baseline, baselineEma: 0.03f, triggerRatio: 1.3f, minFloor: 0.06f);
            Assert.Equal(0f, impact);
        }

        [Fact]
        public void ComputeImpact_SuddenSpikeAboveSteadyBaseline_Triggers()
        {
            float baseline = 0f;
            // 先跑几十帧稳定在 0.2，让 baseline 收敛到接近 0.2
            for (int i = 0; i < 200; i++)
            {
                AudioVisualizerMath.ComputeImpact(0.2f, ref baseline, baselineEma: 0.03f, triggerRatio: 1.3f, minFloor: 0.06f);
            }
            Assert.InRange(baseline, 0.15f, 0.25f);

            // 突然冲到 0.6（远超 baseline 的 1.3 倍），应该判定为一次冲击
            float impact = AudioVisualizerMath.ComputeImpact(0.6f, ref baseline, baselineEma: 0.03f, triggerRatio: 1.3f, minFloor: 0.06f);
            Assert.True(impact > 0f, "明显超出滑动平均 1.3 倍的瞬时值应该被判定为冲击");
        }

        [Fact]
        public void ComputeImpact_SteadyLevel_DoesNotKeepTriggering()
        {
            // 音量稳定不变（没有真的"冲击"）时，baseline 应该逐渐跟上瞬时值，
            // 触发条件（超过 baseline 的 1.3 倍）应该越来越难满足，最终归零
            float baseline = 0f;
            float lastImpact = 0f;
            for (int i = 0; i < 300; i++)
            {
                lastImpact = AudioVisualizerMath.ComputeImpact(0.3f, ref baseline, baselineEma: 0.03f, triggerRatio: 1.3f, minFloor: 0.06f);
            }
            Assert.Equal(0f, lastImpact);
        }

        [Fact]
        public void ComputeImpact_UpdatesBaselineTowardInstant_RegardlessOfTrigger()
        {
            float baseline = 0.5f;
            AudioVisualizerMath.ComputeImpact(0.1f, ref baseline, baselineEma: 0.1f, triggerRatio: 1.3f, minFloor: 0.06f);
            // baseline 应该往瞬时值（更低）靠拢一点，不会保持原地不动
            Assert.True(baseline < 0.5f);
            Assert.True(baseline > 0.1f);
        }
    }
}
