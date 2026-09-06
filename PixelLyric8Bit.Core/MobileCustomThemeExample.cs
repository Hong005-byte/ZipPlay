namespace PixelLyric8BitFix
{
    /// <summary>
    /// 自定义主题的示例 JSON——跟桌面版 PixelLyric8BitFix/CustomThemeExample.cs 是同一份文本（拿现有的
    /// Sunset 皮肤当例子），两边看到的示例得是同一份，不然照着改的人会怀疑是不是哪边写错了。类名
    /// 不叫 CustomThemeExample 是因为桌面版已经有一个同名的 internal 类型，两边在同一命名空间下、
    /// 桌面项目又直接引用 Core，撞名会有二义性编译错误，见 LyricsTranslator.cs 顶部注释同样的教训。
    /// </summary>
    public static class MobileCustomThemeExample
    {
        public const string Json =
@"{
  ""name"": ""我的海边黄昏"",
  ""font"": ""Segoe UI Light"",
  ""colors"": {
    ""title"": ""#FFF3E0"",
    ""artist"": ""#F2C6A0"",
    ""accent"": ""#F9C784"",
    ""lyric"": ""#FFF3E0"",
    ""glow"": ""#F9C784"",
    ""glowBlur"": 4,
    ""lyricBoxBg"": ""#B32A1F40"",
    ""lyricBoxBorder"": ""#F9C784""
  },
  ""background"": {
    ""type"": ""gradient"",
    ""direction"": ""vertical"",
    ""stops"": [""#F2994A"", ""#EA7093"", ""#4A3B78""]
  },
  ""icon"": {
    ""palette"": { ""#"": ""#F9C784"", ""w"": ""#4A3B78"" },
    ""rows"": [
      ""........"",
      ""..####.."",
      "".######."",
      ""########"",
      ""wwwwwwww"",
      ""wwwwwwww"",
      ""........"",
      ""........""
    ]
  },
  ""animation"": { ""type"": ""pulse+sway"", ""duration"": 2.6, ""musicReactive"": true, ""sensitivity"": ""medium"" },
  ""layers"": [
    {
      ""anchor"": ""top-right"",
      ""icon"": {
        ""palette"": { ""o"": ""#FFF3E0"" },
        ""rows"": [ "".oo."", ""o..o"", ""o..o"", "".oo."" ]
      },
      ""animation"": { ""type"": ""twinkle"", ""duration"": 1.8 }
    }
  ]
}";
    }
}
