using System;
using System.IO;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 极简本地日志：出问题时能留点线索，而不是所有 catch 都默默吞掉、事后什么都查不出来。
    /// 只记关键的、非预期的失败（未捕获异常、托盘/热键初始化失败、更新安装失败……），
    /// 不记"四个歌词源里某一个没抓到"这种本来就常态化会发生的事，免得日志被刷屏、真正有用的信息被埋掉。
    /// </summary>
    internal static class AppLog
    {
        private static readonly object _lock = new();

        public static string LogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PixelLyric8BitFix", "log.txt");

        private const long MaxSizeBytes = 1 * 1024 * 1024; // 封顶 1MB，超了就清空重开；日志只是辅助排查，不需要保留历史

        public static void Error(string context, Exception ex) =>
            Write($"[ERROR] {context}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

        public static void Info(string message) => Write($"[INFO] {message}");

        private static void Write(string line)
        {
            try
            {
                lock (_lock)
                {
                    string? dir = Path.GetDirectoryName(LogPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxSizeBytes)
                    {
                        File.Delete(LogPath); // 简单粗暴地重开一个，不做滚动切割
                    }

                    File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
                }
            }
            catch
            {
                // 连日志都写不进去（没权限/磁盘满）就放弃，不能让"记日志"这件事本身搞崩 App
            }
        }
    }
}
