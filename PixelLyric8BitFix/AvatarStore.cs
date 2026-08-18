using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 用户头像：本地存一份缩过尺寸的 PNG（%AppData%\PixelLyric8BitFix\avatar.png），不存原图——
    /// 头像最终显示就巴掌大，没必要留着用户选的原始照片可能有的几 MB 分辨率，缩小了导出个人资料
    /// 文件（ProfileBundle，头像要编码成 base64 塞进同一个 JSON）也能小很多。纯本地，不联网、不上传。
    /// </summary>
    internal static class AvatarStore
    {
        // 头像最终显示尺寸也就几十像素，128 足够清晰，没必要留原图那么大
        private const int MaxDimension = 128;

        public static string AvatarPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PixelLyric8BitFix", "avatar.png");

        public static bool HasAvatar => File.Exists(AvatarPath);

        /// <summary>从用户选的任意图片文件（jpg/png/...）读进来，缩小之后存成本地 PNG。</summary>
        public static bool TrySaveFrom(string sourceFilePath)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(sourceFilePath);
                bitmap.DecodePixelWidth = MaxDimension; // 解码的时候就缩小，不用先读进原始分辨率再缩
                bitmap.CacheOption = BitmapCacheOption.OnLoad; // 立刻读完，不占着源文件的句柄
                bitmap.EndInit();

                string? dir = Path.GetDirectoryName(AvatarPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var fs = File.Create(AvatarPath);
                encoder.Save(fs);
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error("AvatarStore.TrySaveFrom", ex);
                return false;
            }
        }

        public static BitmapImage? Load()
        {
            if (!HasAvatar) return null;
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(AvatarPath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad; // 整份读进内存，读完不占着文件句柄，后面能随时覆盖/删除
                // WPF 有一份按 Uri 存的内部图片缓存，同一个进程里换头像/导入头像之后如果不关掉它，
                // 这次 Load() 可能会命中上一次同一路径的旧缓存、返回换之前的图——头像文件明明已经
                // 覆盖成新的了，界面却还显示旧的，得等重启才刷新
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.EndInit();
                bitmap.Freeze(); // 跨线程/多个控件复用同一个实例的话需要先冻结，顺手做了
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        public static void Clear()
        {
            try { if (File.Exists(AvatarPath)) File.Delete(AvatarPath); }
            catch { /* 删不掉不影响正常使用 */ }
        }

        /// <summary>导出个人资料用：原始 PNG 字节，调用方自己转 base64。</summary>
        public static byte[]? ReadRawBytes() => HasAvatar ? File.ReadAllBytes(AvatarPath) : null;

        /// <summary>导入个人资料用：直接把已经是 PNG 格式的字节写下去，不用再过一遍解码/缩放
        /// （导出的时候已经缩过了，这里假设导入的字节就是同一个格式，省一趟没必要的转换）。</summary>
        public static void WriteRawBytes(byte[] pngBytes)
        {
            try
            {
                string? dir = Path.GetDirectoryName(AvatarPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(AvatarPath, pngBytes);
            }
            catch (Exception ex)
            {
                AppLog.Error("AvatarStore.WriteRawBytes", ex);
            }
        }
    }
}
