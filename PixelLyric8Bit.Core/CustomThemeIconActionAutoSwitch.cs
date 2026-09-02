using System.Collections.Generic;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 数据驱动的动作自动切换——纯决策逻辑，不碰 UI/播放状态（那些是 MainWindow.ListeningStats.cs 的
    /// _customIconContinuousTrackSeconds + MainWindow.Skins.cs 的 EvaluateAutoSwitchIconAction）。
    /// 单独抽出来是因为"挑阈值已经被跨过的里面数值最大的那个、而且只能往前推不能往回拉"这条规则本身
    /// 有不少边界情况（阈值乱序、并列、还没跨过任何阈值……）值得单元测试，而运行时那部分依赖真实的
    /// WPF 窗口/播放状态，没法直接测。跟 PixelIconEditor/ListeningStatsAggregator 是同一个思路：
    /// 纯函数留在 Core 里测，I/O 或 UI 相关的粘合代码留在 WPF 项目里。
    /// </summary>
    public static class CustomThemeIconActionAutoSwitch
    {
        /// <summary>给定当前歌曲已经连续播放了多久（continuousSeconds）、这份图标的全部动作
        /// （actions，不含"动作 0"）、以及现在正停在第几个动作（currentIndex，0 = 动作 0），
        /// 算出"应该"停在第几个动作。
        ///
        /// 规则：只看每个动作自己的 AutoSwitchAfterSeconds（没填或者非正数的动作不参与，永远不会被
        /// 这个方法选中）；在"阈值已经被 continuousSeconds 跨过"的动作里，挑阈值数值最大的那个
        /// （数值越大代表越靠后才该出现的阶段，不是数组顺序），返回 actions 里它的下标+1（因为
        /// 0 留给动作 0）。算出来的目标如果比 currentIndex 还小或者相等，直接原样返回 currentIndex——
        /// 这个方法只负责"往前推"，不会主动把已经手动点到（或者已经被更早一次调用推到）更靠后动作的
        /// 状态拉回去，调用方不需要自己再判断一遍要不要采纳返回值，返回的就是最终应该停留的位置。</summary>
        public static int GetDesiredActionIndex(IReadOnlyList<CustomThemeIconAction> actions, double continuousSeconds, int currentIndex)
        {
            int bestIndex = -1; // -1 = 还没找到任何一个跨过阈值的动作
            double bestThreshold = -1;
            for (int i = 0; i < actions.Count; i++)
            {
                if (actions[i].AutoSwitchAfterSeconds is not double threshold || threshold <= 0) continue;
                if (continuousSeconds < threshold) continue;
                if (threshold > bestThreshold)
                {
                    bestThreshold = threshold;
                    bestIndex = i + 1; // actions[i] 对应 _customIconActionIndex == i+1（0 留给动作 0）
                }
            }
            return bestIndex > currentIndex ? bestIndex : currentIndex;
        }
    }
}
