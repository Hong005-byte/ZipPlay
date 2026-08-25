using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    /// <summary>自定义主题 animation.sensitivity（low/medium/high）落地的两处纯逻辑：字符串档位换算成倍率
    /// （CustomThemeValidator.SensitivityToMultiplier），以及那个倍率怎么作用在 UpdateMusicReactiveSkin 算出来的
    /// SpeedRatio 上（MainWindow.ApplyReactiveSensitivity）。两个都是不碰 UI/音频硬件的纯函数，直接单测。</summary>
    public class MusicReactiveSensitivityTests
    {
        [Theory]
        [InlineData("low", 0.6)]
        [InlineData("LOW", 0.6)] // 大小写不敏感，跟 animation.type 一样
        [InlineData("high", 1.6)]
        [InlineData("medium", 1.0)]
        [InlineData(null, 1.0)]  // 没填 = medium
        [InlineData("", 1.0)]
        [InlineData("garbage", 1.0)] // 理论上校验已经拦过，这里再确认兜底也是 medium，不是抛异常
        public void SensitivityToMultiplier_MapsExpectedValue(string? sensitivity, double expected)
        {
            Assert.Equal(expected, CustomThemeValidator.SensitivityToMultiplier(sensitivity));
        }

        [Fact]
        public void ApplyReactiveSensitivity_MediumMultiplier_IsIdentity()
        {
            // multiplier=1.0 时必须原样返回 ratio（在 [min,max] 范围内的前提下）——这是保证"没填 sensitivity
            // 的老主题/内置皮肤行为不变"的关键性质，这个字段加进来之前就是直接把 ratio 传给 SetSpeedRatio
            double ratio = 1.85;
            double result = MainWindow.ApplyReactiveSensitivity(ratio, baseRatio: 0.7, sensitivityMultiplier: 1.0, min: 0.4, max: 2.6);
            Assert.Equal(ratio, result, precision: 10);
        }

        [Fact]
        public void ApplyReactiveSensitivity_LowMultiplier_PullsCloserToBaseline()
        {
            // ratio 比 baseline 高出 1.15（1.85 - 0.7），multiplier=0.6 应该只保留 60% 的偏移量：
            // 0.7 + 1.15*0.6 = 1.39——比原始 ratio 更接近静息速度，观感上"反应更克制"
            double result = MainWindow.ApplyReactiveSensitivity(1.85, baseRatio: 0.7, sensitivityMultiplier: 0.6, min: 0.4, max: 2.6);
            Assert.Equal(1.39, result, precision: 10);
        }

        [Fact]
        public void ApplyReactiveSensitivity_HighMultiplier_PushesFartherFromBaseline()
        {
            // 同样的偏移量，multiplier=1.6 放大到 160%：0.7 + 1.15*1.6 = 2.54，比原始 ratio 更夸张
            double result = MainWindow.ApplyReactiveSensitivity(1.85, baseRatio: 0.7, sensitivityMultiplier: 1.6, min: 0.4, max: 2.6);
            Assert.Equal(2.54, result, precision: 10);
        }

        [Fact]
        public void ApplyReactiveSensitivity_HighMultiplier_ClampsToMax()
        {
            // 放大后可能冲出 [min,max]——high 挡最容易出现这种情况（本来就快，还要再放大），
            // 必须夹回上限，不能让某个皮肤的动画转到肉眼觉得离谱的速度
            double result = MainWindow.ApplyReactiveSensitivity(2.6, baseRatio: 0.7, sensitivityMultiplier: 1.6, min: 0.4, max: 2.6);
            Assert.Equal(2.6, result, precision: 10);
        }

        [Fact]
        public void ApplyReactiveSensitivity_AtBaseline_UnaffectedByMultiplier()
        {
            // ratio 正好等于 baseRatio（比如没声音的时候）——不管 multiplier 是多少，偏移量本来就是 0，
            // 乘完还是 0，结果始终是 baseRatio。这个性质保证了"安静的时候三档灵敏度看起来都一样"，
            // 差异只在音乐响起来、ratio 偏离 baseline 之后才会显现
            Assert.Equal(0.7, MainWindow.ApplyReactiveSensitivity(0.7, 0.7, 0.6, 0.4, 2.6), precision: 10);
            Assert.Equal(0.7, MainWindow.ApplyReactiveSensitivity(0.7, 0.7, 1.6, 0.4, 2.6), precision: 10);
        }
    }
}
