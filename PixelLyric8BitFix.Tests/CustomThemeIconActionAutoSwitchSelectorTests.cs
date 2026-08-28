using System.Collections.Generic;
using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests
{
    /// <summary>CustomThemeIconActionAutoSwitch.GetDesiredActionIndex——纯决策逻辑：给连续播放秒数 +
    /// 动作列表 + 当前索引，算出"应该"停在第几个动作。见该方法本身的注释了解完整规则；这里覆盖它
    /// 提到的几类边界情况。</summary>
    public class CustomThemeIconActionAutoSwitchSelectorTests
    {
        private static CustomThemeIconAction Action(double? autoSwitchAfterSeconds) =>
            new() { AutoSwitchAfterSeconds = autoSwitchAfterSeconds, Frames = new List<List<string>> { new() { "." } } };

        [Fact]
        public void NoActionsHaveThreshold_StaysAtCurrentIndex()
        {
            var actions = new List<CustomThemeIconAction> { Action(null), Action(null) };
            Assert.Equal(0, CustomThemeIconActionAutoSwitch.GetDesiredActionIndex(actions, continuousSeconds: 999, currentIndex: 0));
        }

        [Fact]
        public void ThresholdNotYetReached_StaysAtCurrentIndex()
        {
            var actions = new List<CustomThemeIconAction> { Action(120) };
            Assert.Equal(0, CustomThemeIconActionAutoSwitch.GetDesiredActionIndex(actions, continuousSeconds: 60, currentIndex: 0));
        }

        [Fact]
        public void ThresholdExactlyReached_Advances()
        {
            // 边界值本身就该算"到了"，不是要严格大于
            var actions = new List<CustomThemeIconAction> { Action(120) };
            Assert.Equal(1, CustomThemeIconActionAutoSwitch.GetDesiredActionIndex(actions, continuousSeconds: 120, currentIndex: 0));
        }

        [Fact]
        public void SingleThresholdCrossed_AdvancesToThatAction()
        {
            var actions = new List<CustomThemeIconAction> { Action(60) };
            Assert.Equal(1, CustomThemeIconActionAutoSwitch.GetDesiredActionIndex(actions, continuousSeconds: 90, currentIndex: 0));
        }

        [Fact]
        public void MultipleThresholdsCrossed_PicksLargestThresholdValue_NotArrayPosition()
        {
            // 阈值大的（120，在数组第 1 个位置，index 0）比阈值小的（60，在数组第 2 个位置，index 1）
            // 更"靠后"——挑的是阈值数值本身，不是它在数组里排第几个
            var actions = new List<CustomThemeIconAction> { Action(120), Action(60) };
            int desired = CustomThemeIconActionAutoSwitch.GetDesiredActionIndex(actions, continuousSeconds: 200, currentIndex: 0);
            Assert.Equal(1, desired); // actions[0]（阈值 120）对应 index 1
        }

        [Fact]
        public void ManualClickBackBelowHighWaterMark_IsNotFoughtByAutoSwitch()
        {
            // 回归测试：这里的 currentIndex 参数在 MainWindow.Skins.cs 里传的其实是"曾经到过的最远
            // 动作"（_customIconAutoSwitchHighWaterMark），不是"当前正显示的动作"——这个区分是修复
            // 一个真实 bug 用的：用户点击手动切回一个更早的动作之后，如果拿"当前正显示的动作"当基准，
            // 下一次评估会立刻把用户刚点回去的选择弹回来（因为连续播放时长早就够格待在更靠后的动作），
            // 50ms 一次的 tick 快到用户感觉不出点击生效过，就像点了没反应。用""曾经到过的最远""当基准
            // 就不会有这个问题：只要这次算出来的阶段没有超过""曾经到过的最远""，这个方法就不该给出
            // 一个比它还大的值——调用方看到"没有更大"就不会去动用户手动选的那个动作。
            var actions = new List<CustomThemeIconAction> { Action(60), Action(120) };

            // 时间已经到了 66.86 秒——单看这份连续播放时长，"应该"停在动作 2（跨过了两个阈值中较大的
            // 那个，120 那个阈值实际没到，这里改用一个更简单的例子：跨过阈值 60 对应动作 1）
            // 用户已经到过动作 1（highWaterMark=1），后来手动点回了动作 0——highWaterMark 不会因为
            // 点击而降低，还是 1
            int desired = CustomThemeIconActionAutoSwitch.GetDesiredActionIndex(actions, continuousSeconds: 90, currentIndex: /* highWaterMark */ 1);

            Assert.Equal(1, desired); // 跟 highWaterMark 打平，不会给出更大的值去覆盖用户刚点的选择
        }

        [Fact]
        public void StagedProgression_AdvancesOneStageAtATime()
        {
            var actions = new List<CustomThemeIconAction> { Action(60), Action(120) };

            Assert.Equal(0, CustomThemeIconActionAutoSwitch.GetDesiredActionIndex(actions, continuousSeconds: 30, currentIndex: 0));
            Assert.Equal(1, CustomThemeIconActionAutoSwitch.GetDesiredActionIndex(actions, continuousSeconds: 60, currentIndex: 0));
            Assert.Equal(2, CustomThemeIconActionAutoSwitch.GetDesiredActionIndex(actions, continuousSeconds: 120, currentIndex: 1));
        }

        [Fact]
        public void NeverRetreats_AlreadyPastHigherStage_StaysThere()
        {
            // 已经在动作 2（比如用户手动点过去，或者上一次调用已经推过去了），继续播放但秒数其实
            // 只够到动作 1 的阈值——不该被拉回动作 1
            var actions = new List<CustomThemeIconAction> { Action(60), Action(120) };
            Assert.Equal(2, CustomThemeIconActionAutoSwitch.GetDesiredActionIndex(actions, continuousSeconds: 70, currentIndex: 2));
        }

        [Fact]
        public void NeverRetreats_ManuallyClickedAheadOfWhatThresholdsWouldGive()
        {
            // 用户手动点到了动作 2，但目前的连续播放秒数其实一个阈值都没跨过——不该把用户拉回动作 0
            var actions = new List<CustomThemeIconAction> { Action(120) };
            Assert.Equal(2, CustomThemeIconActionAutoSwitch.GetDesiredActionIndex(actions, continuousSeconds: 5, currentIndex: 2));
        }

        [Fact]
        public void ActionsWithoutThreshold_AreIgnoredEvenIfCrossedByArrayPosition()
        {
            // 第一个动作没配 autoSwitchAfterSeconds（纯手动动作），第二个配了——不该被第一个"挡住"
            var actions = new List<CustomThemeIconAction> { Action(null), Action(60) };
            Assert.Equal(2, CustomThemeIconActionAutoSwitch.GetDesiredActionIndex(actions, continuousSeconds: 90, currentIndex: 0));
        }

        [Fact]
        public void NonPositiveThreshold_IsIgnored()
        {
            // 理论上校验已经挡住了非正数，这里只是防御：万一真的出现也不该被当成"已经跨过"
            var actions = new List<CustomThemeIconAction> { Action(0), Action(-5) };
            Assert.Equal(0, CustomThemeIconActionAutoSwitch.GetDesiredActionIndex(actions, continuousSeconds: 1000, currentIndex: 0));
        }

        [Fact]
        public void EmptyActionsList_StaysAtCurrentIndex()
        {
            Assert.Equal(0, CustomThemeIconActionAutoSwitch.GetDesiredActionIndex(new List<CustomThemeIconAction>(), continuousSeconds: 1000, currentIndex: 0));
        }
    }
}
