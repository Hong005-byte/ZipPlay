namespace PixelLyric8BitFix
{
    /// <summary>
    /// 纯数据的 ARGB 颜色，不依赖任何 UI 框架——给 <see cref="CustomThemeValidator"/> 这些搬进
    /// PixelLyric8Bit.Core 的纯逻辑用，原来这些地方用的是 System.Windows.Media.Color（WPF 专属，
    /// Android/Uno 那边没有这个类型）。WPF 那边要显示颜色的地方，用一行 Color.FromArgb(c.A, c.R, c.G, c.B)
    /// 转一下就行，见 PixelLyric8BitFix 项目里的 CustomThemeColorInterop。
    /// </summary>
    public readonly record struct RgbaColor(byte A, byte R, byte G, byte B)
    {
        public static readonly RgbaColor Transparent = new(0, 0, 0, 0);
    }
}
