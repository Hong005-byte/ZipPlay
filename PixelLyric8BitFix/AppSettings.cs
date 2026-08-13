using System;
using System.IO;
using Newtonsoft.Json;

namespace PixelLyric8BitFix
{
    public enum PlayerSkin
    {
        Minecraft,
        Simple,
        Crt,        // 复古 CRT 终端风
        Cyberpunk,  // 霓虹赛博朋克风
        Vinyl,      // 黑胶唱片机风
        Glass,      // 玻璃拟态（Glassmorphism）
        Lofi,       // 复古咖啡馆 / lofi 风
        Aurora,     // 极光 / 雪夜风
        Rain,       // 雨夜窗景风
        Starry,     // 星空太空风
        Campfire,   // 篝火露营风
        Sakura,     // 樱花风
        Cassette,   // 复古磁带机风
    }

    public enum PlayerSize
    {
        Small,
        Medium,
        Large,
    }

    public enum PlayerDisplayMode
    {
        Minimal,   // 只显示当前歌词
        Standard,  // 标题 + 艺人 + 时间 + 进度条 + 歌词
    }

    /// <summary>
    /// 启动设置页选出的偏好，落地为本地 JSON 文件，下次启动记住上次的选择。
    /// 不涉及账号/登录，纯本地小组件不需要那一套。
    /// </summary>
    public class AppSettings
    {
        public PlayerSkin Skin { get; set; } = PlayerSkin.Minecraft;
        public PlayerSize Size { get; set; } = PlayerSize.Medium;
        public PlayerDisplayMode DisplayMode { get; set; } = PlayerDisplayMode.Standard;

        private static string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PixelLyric8BitFix",
            "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var loaded = JsonConvert.DeserializeObject<AppSettings>(json);
                    if (loaded != null) return loaded;
                }
            }
            catch
            {
                // 配置文件损坏或读不到，直接回落到默认值，不影响启动
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                string? dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch
            {
                // 保存失败（比如没权限）也不应该让 App 崩溃，下次启动就再用一次默认值
            }
        }

        /// <summary>窗口尺寸预设。选完之后主窗口会锁死大小，不允许再拖拽缩放。</summary>
        public (double Width, double Height) GetWindowSize() => Size switch
        {
            PlayerSize.Small => (400, 170),
            PlayerSize.Large => (760, 340),
            _ => (580, 260), // Medium
        };
    }
}
