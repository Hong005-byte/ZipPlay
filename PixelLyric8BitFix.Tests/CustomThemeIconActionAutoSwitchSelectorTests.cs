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
