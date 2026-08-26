namespace PixelLyric8BitFix
{
    /// <summary>
    /// 自定义主题的示例 JSON——拿现有的 Sunset 皮肤当例子（现成的、已经在用的配色，比瞎编一份更有
    /// 说服力，也顺便验证了"内置皮肤"和"客制化皮肤"走的是同一套字段，不是另外发明了一套格式）。
    /// 抽成独立文件是因为现在有两个地方要用同一份示例：CustomThemeWindow 里的只读示例框，
    /// 和 CustomThemeSpecDoc 里"完整详细说明"文档末尾的示例——两边必须是同一份文本，不然读者
    /// 对着示例框改出来的东西跟文档里说的对不上，容易怀疑是不是哪边写错了。
    /// </summary>
    internal static class CustomThemeExample
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
