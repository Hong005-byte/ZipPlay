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
        public static readonly RgbaColor Black = new(255, 0, 0, 0);
        public static readonly RgbaColor White = new(255, 255, 255, 255);

        /// <summary>给一块纯色背景挑一个看得清的字色（非黑即白）——Mobile 的皮肤/自定义主题选择器
        /// 拿皮肤的 Accent 色直接当按钮背景用，皮肤自己配的 Text 色是设计给"深色悬浮窗背景"上显示
        /// 用的，跟 Accent 经常撞色撞得看不清（真机上踩过：极光雪夜/樱花/复古磁带机这几套皮肤的按钮，
        /// 浅底配浅字，字基本看不见）。这里不管皮肤自己的 Text 是什么颜色，纯按 Accent 这块背景的
        /// 感知亮度（ITU-R BT.601 加权，人眼对绿色最敏感、蓝色最不敏感，比简单平均更准）挑黑或白，
        /// 保证至少能看清，不追求跟皮肤配色协调。</summary>
        public static RgbaColor PickReadableForeground(RgbaColor background)
        {
            double perceivedBrightness = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
            return perceivedBrightness > 0.6 ? Black : White;
        }
    }
}
