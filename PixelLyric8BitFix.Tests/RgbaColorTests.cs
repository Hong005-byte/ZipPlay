using PixelLyric8BitFix;
using Xunit;

namespace PixelLyric8BitFix.Tests;

public class RgbaColorTests
{
    [Fact]
    public void PickReadableForeground_DarkBackground_ReturnsWhite()
    {
        var background = new RgbaColor(255, 0x12, 0x00, 0x22); // 赛博朋克风的悬浮窗背景色，很暗
        Assert.Equal(RgbaColor.White, RgbaColor.PickReadableForeground(background));
    }

    [Fact]
    public void PickReadableForeground_LightBackground_ReturnsBlack()
    {
        var background = new RgbaColor(255, 0xF9, 0xC7, 0x84); // 尊贵皇冠风的金色 Accent，很亮
        Assert.Equal(RgbaColor.Black, RgbaColor.PickReadableForeground(background));
    }

    [Fact]
    public void PickReadableForeground_GreenAccent_ReturnsColorDistinctFromItself()
    {
        // 真机上踩到的具体案例：复古 CRT 终端风的 Accent 是偏亮的绿色 (0x33,0xFF,0x66)，
        // 皮肤自己配的 Text 色 (0x66,0xFF,0xAA) 跟它太接近，字几乎融进背景里看不清——
        // PickReadableForeground 应该挑出黑色，不是继续挑一个同样偏亮的颜色
        var accent = new RgbaColor(255, 0x33, 0xFF, 0x66);
        Assert.Equal(RgbaColor.Black, RgbaColor.PickReadableForeground(accent));
    }
}
