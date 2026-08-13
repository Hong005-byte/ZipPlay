using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace PixelLyric8BitFix
{
    /// <summary>某个已保存的客制化主题：文件名（用来当 AppSettings.CustomThemeFile 的 key）+ 解析出来的数据。</summary>
    public sealed record CustomThemeEntry(string FileName, CustomTheme Theme);

    /// <summary>
    /// 客制化主题的磁盘存取：一个主题一个 JSON 文件，最多 5 个（够用了，多了选择器也摆不下）。
    /// 到上限了必须先删一个才能再存新的——不做"自动挤掉最老的"这种会让用户没提防丢东西的行为。
    /// </summary>
    internal static class CustomThemeStore
    {
        public const int MaxThemes = 5;

        public static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PixelLyric8BitFix", "custom_themes");

        /// <summary>列出所有存好的主题，解析失败的（比如文件被外部改坏了）直接跳过，不让一个坏文件拖垮整个列表。</summary>
        public static List<CustomThemeEntry> ListAll()
        {
            var result = new List<CustomThemeEntry>();
            try
            {
                if (!Directory.Exists(Dir)) return result;
                foreach (var path in Directory.GetFiles(Dir, "*.json").OrderBy(p => p))
                {
                    try
                    {
                        string json = File.ReadAllText(path);
                        var (theme, errors) = CustomThemeValidator.ParseAndValidate(json);
                        if (theme != null && errors.Count == 0)
                        {
                            result.Add(new CustomThemeEntry(Path.GetFileName(path), theme));
                        }
                    }
                    catch { /* 单个文件读/解析失败就跳过，不影响其它主题 */ }
                }
            }
            catch { /* 目录本身有问题就返回空列表 */ }
            return result;
        }

        /// <summary>给"编辑"用：拿回原始 JSON 文本本身，而不是解析完再序列化回去（保留用户自己的格式/缩进）。</summary>
        public static string? LoadRawJson(string fileName)
        {
            try
            {
                string path = Path.Combine(Dir, fileName);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch
            {
                return null;
            }
        }

        public static CustomTheme? Load(string fileName)
        {
            try
            {
                string path = Path.Combine(Dir, fileName);
                if (!File.Exists(path)) return null;
                var (theme, errors) = CustomThemeValidator.ParseAndValidate(File.ReadAllText(path));
                return errors.Count == 0 ? theme : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 新建一个主题。fileName 为 null 表示新建（自动生成文件名）；传已有文件名表示覆盖更新那一份。
        /// 只有新建的时候才检查"是不是已经 5 个了"，更新已有的不受这个限制。
        /// </summary>
        public static (bool Success, string? Error, string? SavedFileName) Save(CustomTheme theme, string? existingFileName)
        {
            try
            {
                Directory.CreateDirectory(Dir);

                if (existingFileName == null && ListAll().Count >= MaxThemes)
                {
                    return (false, $"最多只能存 {MaxThemes} 个客制化主题，先删掉一个再来。", null);
                }

                string fileName = existingFileName ?? $"{Guid.NewGuid():N}.json";
                string path = Path.Combine(Dir, fileName);
                string json = JsonConvert.SerializeObject(theme, Formatting.Indented);
                File.WriteAllText(path, json);
                return (true, null, fileName);
            }
            catch (Exception ex)
            {
                AppLog.Error("CustomThemeStore.Save", ex);
                return (false, "保存失败（可能是磁盘空间或权限问题），详情已经记到日志文件里了。", null);
            }
        }

        public static void Delete(string fileName)
        {
            try
            {
                string path = Path.Combine(Dir, fileName);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                AppLog.Error("CustomThemeStore.Delete", ex);
            }
        }
    }
}
