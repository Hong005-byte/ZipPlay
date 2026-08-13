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
        Cloud,      // 云朵漂浮风
        Candle,     // 烛光冥想风
        Plant,      // 绿植角落风
        Sunset,     // 海边黄昏风
        Custom,     // 客制化主题：具体长什么样由 AppSettings.CustomThemeFile 指向的那份 JSON 决定
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

        /// <summary>上次关闭时的窗口位置。null 表示还没存过，走默认的屏幕居中。</summary>
        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }

        /// <summary>
        /// 歌词整体提前/延后的毫秒数（在歌词框上滚轮调）。正数 = 歌词提前显示（追赶落后的字幕），
        /// 负数 = 歌词延后显示。不同播放器/播放源报告的播放位置跟人耳听到的声音之间的固有延迟不一样，
        /// 快歌一句接一句挨得紧，这点延迟就会很明显，慢歌反而感觉不出来——这是给用户自己手动矫正用的。
        /// </summary>
        public int LyricOffsetMs { get; set; } = 0;

        /// <summary>卡拉OK扫光效果开关（左上角齿轮旁边那个 🎤）。默认开启；关了就退回整行一次性切换。</summary>
        public bool KaraokeEffectEnabled { get; set; } = true;

        /// <summary>只有 Skin == PlayerSkin.Custom 时才有意义：指向 custom_themes 文件夹里具体是哪一份 JSON。</summary>
        public string? CustomThemeFile { get; set; }

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
