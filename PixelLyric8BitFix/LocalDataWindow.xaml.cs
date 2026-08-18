using System.Diagnostics;
using System.IO;
using System.Windows;

namespace PixelLyric8BitFix
{
    /// <summary>歌词缓存统计/清空/打开文件夹 + 诊断日志——从 HomeWindow 的"🗄️ 本地数据"格子进来。</summary>
    public partial class LocalDataWindow : Window
    {
        public LocalDataWindow()
        {
            InitializeComponent();
            RefreshCacheInfo();
        }

        // 显示当前缓存了多少首歌词、占多大空间，清空之后重新算一遍
        private void RefreshCacheInfo()
        {
            var (count, bytes) = LyricsCache.GetStats();
            TxtCacheInfo.Text = count == 0 ? "暂无缓存" : $"已缓存 {count} 首歌词，共 {FormatBytes(bytes)}";
        }

        private static string FormatBytes(long bytes) =>
            bytes < 1024 ? $"{bytes} B" :
            bytes < 1024 * 1024 ? $"{bytes / 1024.0:F1} KB" :
            $"{bytes / 1024.0 / 1024.0:F1} MB";

        private void BtnClearCache_Click(object sender, RoutedEventArgs e)
        {
            LyricsCache.Clear();
            RefreshCacheInfo();
        }

        // 让用户自己在资源管理器里看一眼这个文件夹，眼见为实：清空按钮只会动这一个专属子目录
        private void BtnOpenCacheFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(LyricsCache.CacheDir); // 还没缓存过任何歌词时文件夹不存在，先建出来再打开
                Process.Start(new ProcessStartInfo(LyricsCache.CacheDir) { UseShellExecute = true });
            }
            catch
            {
                // 打不开（比如权限问题）不是关键功能，静默失败就好
            }
        }

        // 日志文件还没生成过（一切正常，从来没触发过 AppLog.Error）就退回打开父目录，不会因为文件不存在而报错
        private void BtnOpenLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (File.Exists(AppLog.LogPath))
                {
                    Process.Start(new ProcessStartInfo(AppLog.LogPath) { UseShellExecute = true });
                }
                else
                {
                    string? dir = Path.GetDirectoryName(AppLog.LogPath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
                    }
                }
            }
            catch
            {
                // 打不开不是关键功能，静默失败就好
            }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e) => Close();
    }
}
