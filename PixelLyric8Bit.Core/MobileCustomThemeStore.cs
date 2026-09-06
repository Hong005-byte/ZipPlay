using Newtonsoft.Json;

namespace PixelLyric8BitFix
{
    /// <summary>某个已保存的客制化主题：文件名（当唯一 id 用）+ 解析出来的数据。类名不叫 CustomThemeEntry
    /// 是因为桌面版 CustomThemeStore.cs 里已经有一个同名的 public record，两边在同一命名空间下、
    /// 桌面项目又直接引用 Core，撞名会有二义性编译错误，见 LyricsTranslator.cs 顶部注释同样的教训。</summary>
    public sealed record MobileCustomThemeEntry(string FileName, CustomTheme Theme);

    /// <summary>
    /// 客制化主题的磁盘存取——逻辑照抄桌面版 PixelLyric8BitFix/CustomThemeStore.cs（一个主题一个 JSON
    /// 文件，最多 10 个，到上限了必须先删一个才能再存新的，不做"自动挤掉最老的"），同样的两处改动：
    /// 目录构造函数传进来、不调 AppLog，见该文件顶部注释同样的教训。
    ///
    /// Mobile 端配色只用得上 theme.Colors 里的几个颜色（见 MobileSkinCatalog.FromCustomTheme）；
    /// 像素图标这块 theme.Icon.Rows/Frames 已经画得出来了（见 FloatingOverlayService.ApplySkin/
    /// MobilePixelIconRenderer），>1 帧的 icon.frames 也真机验证过按 frameDuration 循环播放——
    /// 多层装饰（layers）/点击切姿势（icon.actions）/drift-fall-walk 这些桌面版才有的视觉细节，
    /// 精简版悬浮窗还画不出来，但存储/校验这一层是完整的：存的是完整原始 JSON，以后要接上完整
    /// 渲染，数据早就在了，不用用户重新写一遍主题。
    /// </summary>
    public sealed class MobileCustomThemeStore
    {
        public const int MaxThemes = 10;

        private readonly string _dir;

        public MobileCustomThemeStore(string dir) => _dir = dir;

        /// <summary>列出所有存好的主题，解析失败的（比如文件被外部改坏了）直接跳过，不让一个坏文件拖垮整个列表。</summary>
        public List<MobileCustomThemeEntry> ListAll()
        {
            var result = new List<MobileCustomThemeEntry>();
            try
            {
                if (!Directory.Exists(_dir)) return result;
                foreach (var path in Directory.GetFiles(_dir, "*.json").OrderBy(p => p))
                {
                    try
                    {
                        string json = File.ReadAllText(path);
                        var (theme, errors) = CustomThemeValidator.ParseAndValidate(json);
                        if (theme != null && errors.Count == 0)
                        {
                            result.Add(new MobileCustomThemeEntry(Path.GetFileName(path), theme));
                        }
                    }
                    catch { /* 单个文件读/解析失败就跳过，不影响其它主题 */ }
                }
            }
            catch { /* 目录本身有问题就返回空列表 */ }
            return result;
        }

        public CustomTheme? Load(string fileName)
        {
            try
            {
                string path = Path.Combine(_dir, fileName);
                if (!File.Exists(path)) return null;
                var (theme, errors) = CustomThemeValidator.ParseAndValidate(File.ReadAllText(path));
                return errors.Count == 0 ? theme : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>新建一个主题——精简版没有"编辑已有的"这条路径（见 Mobile 那边 MainPage 的说明），
        /// existingFileName 参数还是留着，跟桌面版存取接口形状保持一致，以后要补编辑功能，这一层
        /// 不用改。</summary>
        public (bool Success, string? Error, string? SavedFileName) Save(CustomTheme theme, string? existingFileName = null)
        {
            try
            {
                Directory.CreateDirectory(_dir);

                if (existingFileName == null && ListAll().Count >= MaxThemes)
                {
                    return (false, $"最多只能存 {MaxThemes} 个客制化主题，先删掉一个再来。", null);
                }

                string fileName = existingFileName ?? $"{Guid.NewGuid():N}.json";
                string path = Path.Combine(_dir, fileName);
                string json = JsonConvert.SerializeObject(theme, Formatting.Indented, CustomThemeValidator.SerializerSettings);
                File.WriteAllText(path, json);
                return (true, null, fileName);
            }
            catch
            {
                return (false, "保存失败（可能是磁盘空间或权限问题）。", null);
            }
        }

        public void Delete(string fileName)
        {
            try
            {
                string path = Path.Combine(_dir, fileName);
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // 删不掉（比如文件正被占用）不影响正常使用
            }
        }
    }
}
