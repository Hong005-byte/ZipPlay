using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 给"成就解锁那一刻弹个庆祝 toast"这个功能记一份"已经庆祝过的成就 id"——AchievementCalculator
    /// 的 8 个听歌成就本身是"现算"的（没有独立的已解锁存档，见它自己的文档注释），完全没有"这个成就
    /// 刚刚才解锁"这个瞬间的概念，每次都是从头评估一遍。要在真正解锁的那一刻弹庆祝，就得有个地方
    /// 记"上次检查的时候，这个成就是不是已经点亮过了"，不然每次评估都会把"早就解锁过的"也当成"刚解锁"
    /// 重新庆祝一遍。这个类只做这一件事：拿一份当前的解锁结果，吐出"这次新出现的解锁"，
    /// 顺手把"已经见过"的集合存盘。第一次运行（本地压根没有这份记录，比如刚更新到带这个功能的版本、
    /// 但早就攒了好几个成就）会把当前已解锁的直接记成"见过"、不补放庆祝——不然升级完一开就是一整串
    /// toast 轰炸，体验反而更差。
    /// </summary>
    internal static class AchievementUnlockTracker
    {
        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PixelLyric8BitFix", "achievement_unlock_seen.json");

        /// <summary>传进来当前这一轮的全部成就评估结果，返回"这次相比上次多解锁的那几个"（顺序即传入顺序）。
        /// 没有变化就返回空列表，也不会碰磁盘（避免每次听歌 tick 都无意义地写一次盘）。</summary>
        public static List<AchievementDefinition> DetectNewlyUnlocked(IEnumerable<AchievementProgress> current)
        {
            bool isFirstRun = !File.Exists(FilePath);
            var seen = Load();
            var newly = new List<AchievementDefinition>();
            bool changed = false;

            foreach (var progress in current)
            {
                if (!progress.Unlocked) continue;
                if (!seen.Add(progress.Achievement.Id)) continue; // 已经见过，不是新解锁的

                changed = true;
                if (!isFirstRun) newly.Add(progress.Achievement); // 第一次跑只建基线，不为"早就解锁过的"补放庆祝
            }

            if (changed) Save(seen);
            return newly;
        }

        private static HashSet<string> Load()
        {
            try
            {
                return File.Exists(FilePath)
                    ? JsonConvert.DeserializeObject<HashSet<string>>(File.ReadAllText(FilePath)) ?? new HashSet<string>()
                    : new HashSet<string>();
            }
            catch
            {
                return new HashSet<string>(); // 文件被外部改坏了就当没见过任何成就——最多是把已解锁的又庆祝一轮，不是关键数据
            }
        }

        private static void Save(HashSet<string> seen)
        {
            try
            {
                string? dir = Path.GetDirectoryName(FilePath);
                if (dir != null) Directory.CreateDirectory(dir);
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(seen.OrderBy(id => id)));
            }
            catch (Exception ex)
            {
                AppLog.Error("AchievementUnlockTracker.Save", ex);
            }
        }
    }
}
