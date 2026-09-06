using Newtonsoft.Json;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 给"成就解锁那一刻弹个庆祝提示"这个功能记一份"已经庆祝过的成就 id"——逻辑照抄桌面版
    /// PixelLyric8BitFix/AchievementUnlockTracker.cs（AchievementCalculator 的 8 个成就是"现算"的，
    /// 没有独立已解锁存档，要在真正解锁那一刻弹庆祝，就得有个地方记"上次检查时这个成就是不是已经点亮
    /// 过了"），一样的两处改动：文件路径构造函数传进来、不调 AppLog；类名不叫
    /// AchievementUnlockTracker 是因为桌面项目直接引用 Core，撞名会有二义性编译错误，
    /// 见 LyricsTranslator.cs 顶部注释同样的教训。
    /// </summary>
    public sealed class MobileAchievementUnlockTracker
    {
        private readonly string _filePath;

        public MobileAchievementUnlockTracker(string filePath) => _filePath = filePath;

        /// <summary>传进来当前这一轮的全部成就评估结果，返回"这次相比上次多解锁的那几个"（顺序即传入顺序）。
        /// 没有变化就返回空列表，也不会碰磁盘。</summary>
        public List<AchievementDefinition> DetectNewlyUnlocked(IEnumerable<AchievementProgress> current)
        {
            bool isFirstRun = !File.Exists(_filePath);
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

        private HashSet<string> Load()
        {
            try
            {
                return File.Exists(_filePath)
                    ? JsonConvert.DeserializeObject<HashSet<string>>(File.ReadAllText(_filePath)) ?? new HashSet<string>()
                    : new HashSet<string>();
            }
            catch
            {
                return new HashSet<string>(); // 文件被外部改坏了就当没见过任何成就——最多是把已解锁的又庆祝一轮，不是关键数据
            }
        }

        private void Save(HashSet<string> seen)
        {
            try
            {
                string? dir = Path.GetDirectoryName(_filePath);
                if (dir != null) Directory.CreateDirectory(dir);
                File.WriteAllText(_filePath, JsonConvert.SerializeObject(seen.OrderBy(id => id)));
            }
            catch
            {
                // 写失败不影响正常使用，最多是下次多庆祝一次已经解锁过的成就
            }
        }
    }
}
