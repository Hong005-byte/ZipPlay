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

        // Arcade 加在最后面、不是插进中间——这个枚举的序号会被序列化成 settings.json 里的整数存起来，
        // 插进中间会让 Custom 和它后面的值序号全部往后挪一位，导致所有已经装过旧版本的用户，
        // 存档里的皮肤选择在升级后全部错位（比如原本存的是"客制化"，升级后一读出来变成了别的皮肤）。
        Arcade,     // 复古街机风
        Invaders,   // 8-bit 太空侵略者风（同样加在最后面，理由同上）
        City,       // 城市夜景/地铁风
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

    public enum LyricFontSize
    {
        Small,
        Medium,
        Large,
    }

    /// <summary>Mini 模式那两圈像素粒子对系统音频的敏感程度，见 AudioVisualizer.ApplySensitivity。</summary>
    public enum VisualizerSensitivity
    {
        Low,
        Medium,
        High,
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

        /// <summary>歌词字号。默认中，跟原来的行为保持一致。</summary>
        public LyricFontSize FontSize { get; set; } = LyricFontSize.Medium;

        /// <summary>
        /// 双语歌词开关（左上角 🌐）。默认开启；只有网易云那个引擎抓到的歌词才可能带翻译行，
        /// 抓到的是纯原文（LRCLIB/QQ音乐/酷狗，或者网易云本身没有翻译）时这个开关不起作用，
        /// 单纯是"有翻译就显示"的意愿开关，不代表这首歌一定有翻译。
        /// </summary>
        public bool BilingualLyricsEnabled { get; set; } = true;

        /// <summary>
        /// 启动设置页最下面那个"下次启动跳过这个页面"勾选框。默认开启：只要之前完整跑过一次流程
        /// （<see cref="HasSavedConfig"/> 为 true，即本地已经有 settings.json），开场动画放完就直接
        /// 拿上次的皮肤/尺寸/显示模式进播放器，不用每次都手动点一下"开始播放"。
        /// 全新安装、还没有任何存档时不受这个字段影响，一定会先看到设置页——不能让人第一次打开
        /// 就直接跳过 18 套皮肤选择，那样根本不知道还有别的皮肤/客制化主题这些功能。
        /// 想改皮肤随时能从主播放器窗口右键 / 齿轮图标回到设置页，这个勾选框只影响"启动的时候要不要先经过这一页"。
        /// </summary>
        public bool SkipStartupSettings { get; set; } = true;

        /// <summary>
        /// Mini 模式那两圈像素粒子的开关。默认开启。关掉之后进 Mini 模式完全不会启动系统音频采集——
        /// 这个功能是纯本地 WASAPI 回环采集 + FFT，不联网、不录制、不保存任何音频数据，但"一个 app
        /// 在分析系统正在播放的声音"这件事本身，对隐私敏感的用户来说最好是能自己关掉、也清楚知道
        /// 在发生什么，而不是默默地做。
        /// </summary>
        public bool MiniVisualizerEnabled { get; set; } = true;

        /// <summary>律动灵敏度档位，默认中。见 AudioVisualizer.ApplySensitivity 具体每档改了哪几个参数。
        /// Mini 模式的像素粒子、下面这个"皮肤音乐律动"共用同一档灵敏度——都是同一份音频分析出来的数据，
        /// 没必要让用户分两个地方各调一次。</summary>
        public VisualizerSensitivity VisualizerSensitivity { get; set; } = VisualizerSensitivity.Medium;

        /// <summary>
        /// "皮肤跟着音乐动"总开关：绝大多数内置皮肤（只有 lofi、烛光冥想两套刻意保持安静，不接入），
        /// 加上勾了 musicReactive 的客制化主题，会跟着系统正在播的音乐响度/节奏实时变化（变速/闪光/
        /// 跳一下/抖一下/晃一下，具体动作因皮肤而异，见 MainWindow.SkinInteractions.cs），不再是固定
        /// 周期的循环。用的是跟 Mini 模式同一个 AudioVisualizer，同样是纯本地 WASAPI 回环采集，不联网、
        /// 不录制、不保存音频；默认开启，关掉之后这些皮肤的动画退回固定节奏循环，且不会启动音频采集。
        /// </summary>
        public bool SkinAudioReactiveEnabled { get; set; } = true;

        private static string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PixelLyric8BitFix",
            "settings.json");

        /// <summary>本地是不是已经有存档——判断"这是不是第一次打开"就靠这个，跟 SkipStartupSettings 搭配用。</summary>
        public static bool HasSavedConfig => File.Exists(ConfigPath);

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

        /// <summary>歌词行的字号（磅值）。原来写死是 14（对应 Medium），小/大各加减 3。</summary>
        public double GetLyricFontSizePt() => FontSize switch
        {
            LyricFontSize.Small => 11,
            LyricFontSize.Large => 17,
            _ => 14, // Medium
        };
    }
}
