using System;
using System.IO;
using Newtonsoft.Json;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// "个人资料"打包导出/导入用的数据形状：用户名 + 头像（PNG 字节转 base64，这样一张图片也能塞进
    /// 同一份 JSON，不用额外带一个头像文件）+ 完整的听歌统计。换电脑/重装之后导出一份、在新的地方
    /// 导入回去，名字、头像、听歌记录就都接上了，不用从头再来。纯本地文件操作，不涉及任何网络/账号。
    /// </summary>
    public sealed class ProfileBundle
    {
        public string? UserName { get; set; }
        public string? AvatarPngBase64 { get; set; }
        public ListeningStats? Stats { get; set; }
    }

    /// <summary>ProfileBundle 的磁盘导出/导入，跟 AppSettings/ListeningStatsStore 是同一套"任何异常
    /// 都不让它波及正常使用"的写法。</summary>
    internal static class ProfileBundleStore
    {
        public static ProfileBundle BuildFromLocal()
        {
            var settings = AppSettings.Load();
            var avatarBytes = AvatarStore.ReadRawBytes();
            return new ProfileBundle
            {
                UserName = settings.UserName,
                AvatarPngBase64 = avatarBytes != null ? Convert.ToBase64String(avatarBytes) : null,
                Stats = ListeningStatsStore.Load(),
            };
        }

        /// <summary>把导入的数据整个覆盖到本地——名字、头像、统计数据都是"有就覆盖，没有就保留原样"，
        /// 不会因为导入的文件里某一项是空的就把本地已有的那部分清掉（比如只想换台电脑同步统计数据、
        /// 不想动头像的场景）。</summary>
        public static void ApplyToLocal(ProfileBundle bundle)
        {
            if (!string.IsNullOrWhiteSpace(bundle.UserName))
            {
                var settings = AppSettings.Load();
                settings.UserName = bundle.UserName;
                settings.Save();
            }

            if (!string.IsNullOrEmpty(bundle.AvatarPngBase64))
            {
                try { AvatarStore.WriteRawBytes(Convert.FromBase64String(bundle.AvatarPngBase64)); }
                catch (Exception ex) { AppLog.Error("ProfileBundleStore.ApplyToLocal avatar", ex); }
            }

            if (bundle.Stats != null)
            {
                ListeningStatsStore.Save(bundle.Stats);
            }
        }

        public static void ExportTo(string filePath)
        {
            string json = JsonConvert.SerializeObject(BuildFromLocal(), Formatting.Indented);
            File.WriteAllText(filePath, json);
        }

        /// <summary>解析失败（不是这个格式的文件）返回 null，调用方自己决定怎么提示用户。</summary>
        public static ProfileBundle? TryImportFrom(string filePath)
        {
            try
            {
                string json = File.ReadAllText(filePath);
                return JsonConvert.DeserializeObject<ProfileBundle>(json);
            }
            catch (Exception ex)
            {
                AppLog.Error("ProfileBundleStore.TryImportFrom", ex);
                return null;
            }
        }
    }
}
