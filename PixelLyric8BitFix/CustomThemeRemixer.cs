using Newtonsoft.Json;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// "🔀 混搭已存主题"背后的逻辑：从三份已经存过的主题里各拿一块（配色+背景+字体 / 图标+额外装饰层 /
    /// 动画），拼成一份新的 CustomTheme，再序列化回 JSON 文本塞进输入框——走的是跟 🎲 随机生成一样的
    /// "生成草稿，用户自己接着改"路线，不是直接落盘保存。
    ///
    /// 跟 CustomThemeRandomizer 的关键区别：那边是从内置调色板"从零编"，这边是从用户自己已经存过、
    /// 已经过校验的三份 CustomTheme 对象里直接摘字段——不用重新拼 JSON 字符串（不会有转义踩坑的风险，
    /// 见 CustomThemeSpecDoc 当初栽过的那个坑），也不需要重新校验来源本身合不合法，
    /// CustomThemeStore.ListAll() 拿到手的就已经是校验通过的。
    /// </summary>
    internal static class CustomThemeRemixer
    {
        /// <summary>colorSource 提供 name 的一部分、font、colors、background；iconSource 提供 icon 和
        /// layers（同一个主题的图标和额外装饰通常是配套设计的，一起带走比拆开更协调）；animationSource
        /// 提供 animation。三个来源可以是同一份主题（等于原样复制一份），调用方不需要先判断。</summary>
        public static string Remix(CustomTheme colorSource, CustomTheme iconSource, CustomTheme animationSource)
        {
            var mixed = new CustomTheme
            {
                Name = $"混搭·{colorSource.Name}×{iconSource.Name}×{animationSource.Name}",
                Font = colorSource.Font,
                Colors = colorSource.Colors,
                Background = colorSource.Background,
                Icon = iconSource.Icon,
                Layers = iconSource.Layers,
                Animation = animationSource.Animation,
            };
            return JsonConvert.SerializeObject(mixed, Formatting.Indented, CustomThemeValidator.SerializerSettings);
        }
    }
}
