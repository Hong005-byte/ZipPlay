using System;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// <see cref="AudioVisualizer"/> 里不碰音频硬件、纯数学的那几块单独抽出来——FFT 采集本身没法在
    /// 单元测试里跑真实音频，但"压缩幅度""区间平均""判断是不是一次冲击"这几个函数的边界行为
    /// （比如刚好卡在阈值上、baseline 还是 0 的时候）能钉住，以后再调灵敏度参数不用靠肉眼反复听/看。
    /// </summary>
    internal static class AudioVisualizerMath
    {
        /// <summary>
        /// 把 FFT 幅度压成 0~1：log 压缩，把人眼更敏感的中低能量部分拉开，不然视觉上会显得"要么灭、要么爆表"。
        /// </summary>
        /// <param name="magnitude">这个频段的原始 FFT 幅度（非负）。</param>
        /// <param name="gain">压缩前的放大倍数。</param>
        /// <param name="referenceMagnitude">"满量程"对应的参考幅度——magnitude 等于这个值时压缩结果正好是 1。</param>
        public static float CompressMagnitude(double magnitude, double gain, double referenceMagnitude)
        {
            double compressed = Math.Log10(1 + magnitude * gain) / Math.Log10(1 + referenceMagnitude * gain);
            return (float)Math.Clamp(compressed, 0, 1);
        }

        /// <summary>[start, end) 这一段的平均值；越界/空区间直接返回 0，不抛异常。</summary>
        public static float AverageRange(ReadOnlySpan<float> levels, int start, int end)
        {
            float sum = 0;
            int count = 0;
            for (int i = Math.Max(0, start); i < end && i < levels.Length; i++)
            {
                sum += levels[i];
                count++;
            }
            return count > 0 ? sum / count : 0;
        }

        /// <summary>
        /// baseline 是"最近一段时间大概的能量水平"，用很慢的滑动平均持续跟着当前能量靠拢；
        /// 瞬时能量如果明显冲出这个水平（且本身不算太小），才算一次"冲击"，归一化成 0~1 的强度。
        /// </summary>
        /// <param name="instant">这一帧的瞬时能量。</param>
        /// <param name="baseline">滑动平均，调用方持有，每次调用都会被这个函数原地更新。</param>
        /// <param name="baselineEma">滑动平均的更新速率，越小跟得越慢。</param>
        /// <param name="triggerRatio">瞬时能量要超过 baseline 的这个倍数才算冲击。</param>
        /// <param name="minFloor">瞬时能量的绝对下限——太小（安静段落的正常波动）不该触发。</param>
        public static float ComputeImpact(float instant, ref float baseline, float baselineEma, float triggerRatio, float minFloor)
        {
            float impact = 0f;
            float triggerLevel = baseline * triggerRatio;
            if (instant > minFloor && instant > triggerLevel)
            {
                impact = Math.Clamp((instant - triggerLevel) / (1 - triggerLevel + 0.0001f), 0, 1);
            }
            baseline += (instant - baseline) * baselineEma;
            return impact;
        }
    }
}
