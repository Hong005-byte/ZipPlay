using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 用户自己写的客制化主题——纯数据，不是代码。用户填的是颜色/渐变/图标像素格/挑一个内置动画"招式"，
    /// app 只负责"读数据画图"，用户没法让 app 做数据描述之外的任何事。
    /// 对应"自定义主题"页面里让用户粘贴的那份 JSON。
    /// </summary>
    public sealed class CustomTheme
    {
        public string? Name { get; set; }
        public string? Font { get; set; }
        public CustomThemeColors? Colors { get; set; }
        public CustomThemeBackground? Background { get; set; }
        public CustomThemeIcon? Icon { get; set; }
        public CustomThemeAnimation? Animation { get; set; }

        // 可选，最多 2 个（CustomThemeValidator.MaxLayers）。icon/animation 是"主图标"，固定贴在歌词框
        // 上方那条 50px 的装饰栏（跟内置皮肤那一整排图标位置一样）；layers 是额外叠加的小装饰，各自贴在
        // 卡片四个角上的一个（anchor），不是替换主图标，是在它之上再加。留空/不填完全不影响主图标那一套——
        // 这是这个字段加进来之前唯一的行为，老主题不用改。见 CustomThemeLayer。
        public List<CustomThemeLayer>? Layers { get; set; }
    }

    /// <summary>一个额外装饰层：自己的图标 + 动画 + 贴在卡片哪个角。跟 CustomTheme 顶层的 icon/animation
    /// 是同一套字段形状（复用 CustomThemeIcon/CustomThemeAnimation），只是多了个 Anchor，也不会
    /// 出现"飘过/飘落整张卡片"那种主图标才有的专属轨道——drift/fall 在这里退化成原地小幅摆动，
    /// 具体差异见 MainWindow.Skins.cs 的 StartLayerAnimation。</summary>
    public sealed class CustomThemeLayer
    {
        public string? Anchor { get; set; } // "top-left" / "top-right" / "bottom-left" / "bottom-right"
        public CustomThemeIcon? Icon { get; set; }
        public CustomThemeAnimation? Animation { get; set; }
    }

    public sealed class CustomThemeColors
    {
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Accent { get; set; }
        public string? Lyric { get; set; }
        public string? Glow { get; set; }
        public double GlowBlur { get; set; } = 3;
        public string? LyricBoxBg { get; set; }
        public string? LyricBoxBorder { get; set; }
    }

    public sealed class CustomThemeBackground
    {
        public string? Type { get; set; }        // "solid" | "gradient"
        public string? Direction { get; set; }    // "vertical" | "diagonal"，只有 gradient 用得上
        public List<string>? Stops { get; set; }  // 2~4 个十六进制颜色
    }

    public sealed class CustomThemeIcon
    {
        public Dictionary<string, string>? Palette { get; set; } // 单字符 -> 十六进制颜色，"." 固定是透明，不用填
        public List<string>? Rows { get; set; }                   // 4~64 行，每行必须等宽，宽度也是 4~64（不强制正方形，见 CustomThemeValidator.MinIconSize/MaxIconSize）

        // 可选：给了就是逐帧循环播放（走路/扇翅膀那种"1、2、3 交替出现看起来在动"的效果，
        // 跟 Minecraft 皮肤 Steve 走路换腿是完全同一套机制，只是从写死两张图变成读用户自己画的任意张），
        // 不给就还是上面 Rows 这唯一一帧——完全向后兼容，老主题一个字都不用改。每一帧的形状规则
        // 跟 Rows 完全一样（行数/宽度落在 MinIconSize~MaxIconSize、每行等宽），而且所有帧必须彼此
        // 同宽同高——不然切换的时候图标会跳着缩放/错位，是"抖"不是"动"。帧数不设上限，用户想画几帧
        // 就几帧，没有系统层面的技术瓶颈需要卡这个数字（渲染只是多存几张已经生成好的位图，逐个轮流显示，
        // 不会因为帧多就变卡）。真给了 Frames，渲染只认它、Rows 会被忽略——留着 Rows 字段本身不冲突，
        // 单纯是没必要为了这个特意去掉一个已经存在的字段。
        public List<List<string>>? Frames { get; set; }

        // 每帧播放多久（秒），只有 Frames 有值时才有意义。不填默认见 CustomThemeValidator.DefaultFrameDurationSeconds
        // （0.25，对齐 Steve 换腿大约 250ms 一帧的节奏，不是瞎猜的数字）。
        public double? FrameDuration { get; set; }

        // 可选：额外的可点击切换的"动作"。图标自己的 Rows/Frames 永远是"动作 0"（不用取名字，
        // Mini 小方块/分享卡片/初次显示这些地方都还是只认这一套，不用改），Actions[0]/[1]/……依次排
        // 在后面；点一下装饰图标（见 MainWindow.SkinInteractions.cs 的 CustomIcon_MouseLeftButtonDown）
        // 永久切到下一个，绕完一圈回到动作 0。只换帧，不连带切换 animation.type 那套移动方式——
        // sway/drift 这些是主题选定的单一移动方式，不会因为切了动作就跟着变。不给这个字段（或者给
        // 空数组）就是这个字段加进来之前的样子：图标不会响应点击，双击照样能穿透进 Mini 模式。
        public List<CustomThemeIconAction>? Actions { get; set; }
    }

    /// <summary>一个可点击切换到的额外动作——形状规则跟 CustomThemeIcon.Frames 完全一样（复用同一套
    /// ValidateFrames），只是换了个字段名方便挂在 Actions 列表里。Name 纯粹给人看，报错定位/以后如果
    /// 做画板 UI 选择器会用到，不参与渲染逻辑。</summary>
    public sealed class CustomThemeIconAction
    {
        public string? Name { get; set; }
        public List<List<string>>? Frames { get; set; }
        public double? FrameDuration { get; set; } // 不填就落回 icon 顶层的 FrameDuration（再没有就是默认值）

        // 可选：这个动作自己的移动方式，形状跟顶层 animation 完全一样（type/duration/musicReactive/
        // sensitivity，也包括 drift/fall——点一下切到这个动作，图标会真的搬进/搬出""飘过/飘落卡片""
        // 的专属三重影轨道，不再只是原地换帧，见 MainWindow.Skins.cs 的 ApplyCustomIconMovement）。
        // 不填就是这个字段加进来之前唯一的行为——所有动作共用 icon 顶层那一个 animation，切动作只
        // 换画面不换动法。填了的话是一份完整独立的动画配置（跟 layers[i].animation 一样的""要么不填、
        // 要么整份自己给全""的规则，不是往顶层动画上打补丁——Duration/MusicReactive/Sensitivity 各自
        // 用自己的默认值，不会继承顶层那份的对应字段）。
        //
        // 唯一的限制：drift/fall 不能跟别的招式组合（也不能互相组合），跟顶层 animation 的组合规则
        // 一模一样——那两招各自是整张卡片飘过/飘落的专属轨道，只能单独出现，见 CustomThemeValidator
        // 的 ValidateAnimation。
        public CustomThemeAnimation? Animation { get; set; }

        // 可选：数据驱动的自动切换阈值（秒）——当前正在播的这首歌"连续播放"（暂停不计时，切歌清零，
        // 见 MainWindow.ListeningStats.cs 的 _customIconContinuousTrackSeconds）满这个秒数之后，
        // 自动切到这个动作，不用等用户点。多个动作都设了这个字段的话，取"阈值已经被跨过的里面数值
        // 最大"的那个（数值越大代表越靠后才该出现的阶段，见 MainWindow.Skins.cs 的
        // EvaluateAutoSwitchIconAction）；已经手动点到（或者被更高阶段自动切到）更靠后的动作时不会
        // 被拉回来——只会把索引往前推，不会跟用户已经做出的选择打架。不填就是这个字段加进来之前
        // 唯一的行为：完全靠点击手动切换，不会有任何数据驱动的自动切换发生。
        public double? AutoSwitchAfterSeconds { get; set; }
    }

    public sealed class CustomThemeAnimation
    {
        public string? Type { get; set; }  // pulse / twinkle / drift / fall / bob / sway / spin / flicker
        public double? Duration { get; set; } // 秒，不填就用每种招式自己的默认值

        // 可选，不填默认 false。true 的话，不管上面 Type 选的是 8 种招式里的哪一种，这个动画的播放速度
        // 都会跟着系统正在播的音乐响度/鼓点实时变化——跟内置皮肤（黑胶转速、Minecraft 走路变速……）
        // 用的是同一套机制，见 MainWindow.SkinInteractions.cs 的 UpdateMusicReactiveSkin。
        // 受设置页"皮肤音乐律动"这个总开关控制，那个开关关了的话这里勾了也不会生效。
        public bool MusicReactive { get; set; }

        // 可选，"low" / "medium" / "high"，不填默认 medium（等同 1.0 倍，也是这个字段加进来之前
        // 唯一的行为，老主题不用改就还是原来的速度感）。跟内置皮肤共用的"响度/鼓点算出来的播放速度倍率"
        // 是同一份数据，这个字段只决定这份主题自己的动画对那份数据"放大/缩小多少反应"——low 声音大变化
        // 也比较克制，high 反过来会被小声音放得更明显。具体倍数见 CustomThemeValidator.SensitivityToMultiplier，
        // MusicReactive 为 false 的时候这个字段不生效（没有反应可放大/缩小）。
        public string? Sensitivity { get; set; }
    }

    /// <summary>
    /// 校验一份 <see cref="CustomTheme"/>：哪一项没填、哪一项格式不对，都给出具体到字段的错误信息，
    /// 而不是笼统地说"格式错误"——用户是自己手写 JSON，含糊的报错基本等于没用。
    /// </summary>
    public static class CustomThemeValidator
    {
        public static readonly string[] ValidAnimationTypes = { "pulse", "twinkle", "drift", "fall", "bob", "sway", "spin", "flicker" };
        public static readonly string[] ValidSensitivities = { "low", "medium", "high" };

        // 序列化一个 CustomTheme 对象回 JSON 文本时用这份设置——键名转成 camelCase（"name"/"colors"/
        // "glowBlur"……），保持跟"怎么写"说明、示例 JSON、随机生成器吐出来的文本是同一套命名习惯。
        // 解析那边（ParseAndValidate 用的 JsonConvert.DeserializeObject）本来就不区分大小写，
        // 所以旧主题文件即使是老版本存下来的 PascalCase（"Name"/"Colors"）也照样读得出来，
        // 这份设置只影响"以后新写出去的文件长什么样"，不影响能不能读旧文件。
        //
        // 不能直接用 CamelCasePropertyNamesContractResolver()——它默认连字典的 key 也会一起转
        // camelCase（NamingStrategy.ProcessDictionaryKeys 默认是 true），而 icon.palette / layers[i].
        // icon.palette 这些 Dictionary<string,string> 的 key 是画板/用户自己分配的单字符调色板符号，
        // 大小写是有意义的、彼此独立的两个颜色（PixelIconEditor.AssignableChars 里 'f' 和 'F' 就是分开
        // 分配的两种颜色，撑大调色板上限到 45 种）——被强制转小写会把 "A" 和 "a" 这种本该是两个颜色的
        // key 挤成同一个，序列化直接写出两条重复的 "a" 键，反序列化回来丢一个，图标就此损坏，
        // rows 里引用的那些大写符号在 palette 里找不到对应颜色了。这是真实炸过的 bug：手绘/转换出的
        // 大调色板（16+ 色）图标存下去之后，回读校验会报"icon.rows 里用了字符 'X'，但 icon.palette 里
        // 没有给它配颜色"，导致这份主题从"已保存的客制化主题"列表里静默消失——存的时候看着成功了
        // （存盘前的那次校验用的是内存里没被这层转换污染的原始对象），下次刷新列表重新读盘校验才炸。
        // 显式用 DefaultContractResolver + processDictionaryKeys:false 的命名策略，只转属性名，
        // 字典 key 原样保留。
        public static readonly JsonSerializerSettings SerializerSettings = new()
        {
            ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
            {
                NamingStrategy = new Newtonsoft.Json.Serialization.CamelCaseNamingStrategy(processDictionaryKeys: false, overrideSpecifiedNames: true),
            },
        };

        // 图标网格的行数/每行宽度都必须落在这个范围内，两个方向各自独立判断，不强制正方形——内置皮肤里
        // Steve 就是 8 列 x 16 行的长条形，同一套渲染逻辑（PixelArt.Build）本来就不挑尺寸，渲染这边到
        // 64x64 完全没有技术上的天花板。这两个数纯粹是校验层面的软上限，不是系统限制：MaxIconSize 定这么高
        // 是留给愿意手写更精细图标的人；Mini 小方块/装饰动画那几个展示框（见 MainWindow.xaml 里几个
        // Custom*Icon）还是 20~52px 上下，网格比这个大很多的话，多出来的细节缩小显示时会被压掉看不出来，
        // 纯粹是"画了但看不见"，不是不能用。MinIconSize 保底 4，最省事的 8x8 也一直落在这个范围内。
        public const int MinIconSize = 4;
        public const int MaxIconSize = 64;

        // 逐帧动画（icon.frames）没填 frameDuration 时用这个——对齐 Minecraft 皮肤 Steve 走路换腿大约
        // 250ms 一帧的节奏（见 MainWindow.Skins.cs 的 UpdateSteveWalkAnimation），不是瞎猜的数字，
        // 保证不特意调这个字段的话，自定义图标的换帧手感跟内置的 Steve 走路观感是一致的。
        public const double DefaultFrameDurationSeconds = 0.25;

        // 额外装饰层：最多 2 个，贴在卡片四个角之一。上限故意压得比 10 个已存主题的上限低很多——
        // 层数一多，渲染开销（每层一份独立的 BuildCustomIcon + 一套动画）线性往上涨，2 个已经够表达
        // "主图标 + 一两个点缀"这种常见组合（内置皮肤里最多的樱花/都市夜景也就 2 个动态元素），
        // 真需要更复杂的场景，本来就更适合做成新的内置皮肤，不是客制化主题这条轻量路径该扛的
        public const int MaxLayers = 2;
        public static readonly string[] ValidAnchors = { "top-left", "top-right", "bottom-left", "bottom-right" };

        // icon.actions 数量上限——纯粹是校验层面给个保守的圆整数字（跟 CustomThemeStore.MaxThemes
        // 那种做法一致），不是系统层面的技术瓶颈。切换动作是点一下循环到下一个，10 个已经够表达
        // "好几套姿势轮流切换"这种场景，真需要更多更适合考虑别的表达方式（比如干脆做成新皮肤）。
        public const int MaxIconActions = 10;

        public static (CustomTheme? Theme, List<string> Errors) ParseAndValidate(string json)
        {
            var errors = new List<string>();
            CustomTheme? theme;

            try
            {
                // 先按严格模式解析：JSON 本身写错了（少个逗号、引号没配对之类）直接在这一步报出来，
                // 比丢给业务校验逻辑之后报一堆"字段缺失"要清楚得多
                theme = JsonConvert.DeserializeObject<CustomTheme>(json, new JsonSerializerSettings
                {
                    MissingMemberHandling = MissingMemberHandling.Ignore,
                });
            }
            catch (JsonException ex)
            {
                errors.Add($"JSON 格式本身有问题，检查一下有没有漏逗号/少引号/括号没配对：{ex.Message}");
                return (null, errors);
            }

            if (theme == null)
            {
                errors.Add("解析出来是空的，确认粘贴的内容不是空白。");
                return (null, errors);
            }

            if (string.IsNullOrWhiteSpace(theme.Name))
            {
                errors.Add("\"name\" 不能为空——这是要显示在主题选择器里的名字。");
            }

            ValidateColor(theme.Colors?.Title, "colors.title", errors);
            ValidateColor(theme.Colors?.Artist, "colors.artist", errors);
            ValidateColor(theme.Colors?.Accent, "colors.accent", errors);
            ValidateColor(theme.Colors?.Lyric, "colors.lyric", errors);
            ValidateColor(theme.Colors?.Glow, "colors.glow", errors, allowEmpty: true);
            ValidateColor(theme.Colors?.LyricBoxBg, "colors.lyricBoxBg", errors);
            ValidateColor(theme.Colors?.LyricBoxBorder, "colors.lyricBoxBorder", errors);

            if (theme.Background == null)
            {
                errors.Add("\"background\" 整块都没填。");
            }
            else
            {
                string? bgType = theme.Background.Type?.ToLowerInvariant();
                if (bgType != "solid" && bgType != "gradient")
                {
                    errors.Add("\"background.type\" 只能是 \"solid\" 或 \"gradient\"。");
                }

                if (theme.Background.Stops == null || theme.Background.Stops.Count == 0)
                {
                    errors.Add("\"background.stops\" 至少要填 1 个颜色（gradient 建议 2~4 个）。");
                }
                else
                {
                    if (bgType == "gradient" && theme.Background.Stops.Count < 2)
                    {
                        errors.Add("\"background.type\" 是 gradient 的话，\"background.stops\" 至少要 2 个颜色才有渐变效果。");
                    }
                    for (int i = 0; i < theme.Background.Stops.Count; i++)
                    {
                        ValidateColor(theme.Background.Stops[i], $"background.stops[{i}]", errors);
                    }
                }
            }

            if (theme.Icon == null)
            {
                errors.Add("\"icon\" 整块都没填——Mini 小方块和皮肤装饰用的像素图标。");
            }
            else
            {
                ValidateIcon(theme.Icon, errors);
            }

            // 主图标不许 drift/fall 跟别的招式组合（这两招用的是整张卡片飘过/飘落的专属轨道，跟主图标
            // 固定在装饰栏这件事本身互斥），层不受这个限制——见 ValidateAnimation 的 allowDriftFallCombo 参数
            ValidateAnimation(theme.Animation, "animation", errors, allowDriftFallCombo: false);

            // 注：以前这里还有一条"顶层是 drift/fall 的话任何动作都不能单独指定 animation"的交叉检查——
            // 那是在动作还没法自己选 drift/fall 的阶段留下的限制，图标只有唯一一条固定轨道，点击切动作
            // 没法把图标搬进搬出。现在动作自己的 animation 也能选 drift/fall 了（MainWindow.Skins.cs
            // 的 ApplyCustomIconMovement 每次切动作都会重新决定图标该待在哪条轨道），这条限制已经没有
            // 存在的理由，删掉了——顶层随便选、每个动作也各自随便选，互不冲突。

            if (theme.Layers != null)
            {
                if (theme.Layers.Count > MaxLayers)
                {
                    errors.Add($"\"layers\" 最多只能有 {MaxLayers} 个，现在是 {theme.Layers.Count} 个。");
                }
                for (int i = 0; i < theme.Layers.Count; i++)
                {
                    var layer = theme.Layers[i];
                    string prefix = $"layers[{i}]";

                    string? anchor = layer.Anchor;
                    if (string.IsNullOrWhiteSpace(anchor))
                    {
                        errors.Add($"\"{prefix}.anchor\" 没填，必须是这几种之一：" + string.Join(" / ", ValidAnchors));
                    }
                    else if (!ValidAnchors.Contains(anchor.ToLowerInvariant()))
                    {
                        errors.Add($"\"{prefix}.anchor\" 填的是 \"{anchor}\"，只能是：" + string.Join(" / ", ValidAnchors));
                    }

                    if (layer.Icon == null)
                    {
                        errors.Add($"\"{prefix}.icon\" 整块都没填。");
                    }
                    else
                    {
                        ValidateIcon(layer.Icon, errors, prefix);
                    }

                    // 层没有独立的三图标飘过/飘落轨道——drift/fall 在层里走的是跟其它 6 招同一套单图标
                    // 渲染（见 MainWindow.Skins.cs 的 StartLayerAnimation），没有主图标那个结构性冲突，
                    // 可以自由组合
                    ValidateAnimation(layer.Animation, $"{prefix}.animation", errors, allowDriftFallCombo: true);
                }
            }

            return (errors.Count == 0 ? theme : null, errors);
        }

        /// <summary>animation.type 现在可以是"pulse"这种单招，也可以是"pulse+sway"这种用 + 连起来的组合——
        /// 渲染那边（MainWindow.Skins.cs / CustomThemeWindow.xaml.cs）都要按同一个规则切开，抽出来共用一份，
        /// 不然两边对"怎么切、切完要不要 trim/小写"这些细节容易走岔。空白项（比如手滑打了个 "pulse+"）
        /// 会被过滤掉，不当成一个空字符串招式。</summary>
        public static string[] SplitAnimationTypes(string type) =>
            type.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.ToLowerInvariant())
                .ToArray();

        // 主图标的 animation 和每个 layers[i].animation 是同一套校验规则（type 是 8 招组合、duration 是正数、
        // sensitivity 三档之一），抽出来共用一份，不然多层加进来之后同一段逻辑要复制 MaxLayers+1 遍。
        // allowDriftFallCombo=false（主图标）时 drift/fall 必须单独出现，不能跟别的招式（也不能跟彼此）
        // 组合——这两招用的是整张卡片飘过/飘落的专属轨道（三份图标各自动画），主图标同时又要固定显示在
        // 装饰栏里，两种视觉结构互斥，"drift+pulse"这种组合没法同时画出来。层没有这个专属轨道，
        // 全部走同一套单图标渲染，allowDriftFallCombo=true 时组合不受限制。
        private static void ValidateAnimation(CustomThemeAnimation? animation, string fieldPrefix, List<string> errors, bool allowDriftFallCombo)
        {
            if (animation == null || string.IsNullOrWhiteSpace(animation.Type))
            {
                errors.Add($"\"{fieldPrefix}.type\" 没填，必须是这几种之一（也可以用 + 组合多个，比如 \"pulse+sway\"）：" + string.Join(" / ", ValidAnimationTypes));
            }
            else
            {
                string[] types = SplitAnimationTypes(animation.Type);
                if (types.Length == 0)
                {
                    errors.Add($"\"{fieldPrefix}.type\" 填的是 \"{animation.Type}\"，切出来一个有效招式都没有。");
                }
                foreach (var t in types)
                {
                    if (!ValidAnimationTypes.Contains(t))
                    {
                        errors.Add($"\"{fieldPrefix}.type\" 里的 \"{t}\" 不认识，只能是：" + string.Join(" / ", ValidAnimationTypes));
                    }
                }
                if (!allowDriftFallCombo && types.Length > 1 && types.Any(t => t is "drift" or "fall"))
                {
                    errors.Add($"\"{fieldPrefix}.type\" 里的 drift/fall 不能跟别的招式组合（这两招是整张卡片的飘过/飘落轨道，跟主图标固定显示在装饰栏这件事结构上冲突），只能单独用，比如 \"drift\"；额外装饰层（layers）里没有这个限制。");
                }
            }

            // duration 不填就用默认值，但填了的话必须是正数——0 或负数会让 WPF 的动画系统在播放时直接抛异常崩溃
            if (animation?.Duration is double d && d <= 0)
            {
                errors.Add($"\"{fieldPrefix}.duration\" 填的是 {d}，必须是大于 0 的数字（不填就用默认值）。");
            }

            // sensitivity 不填就是 medium，填了的话必须是三档之一——大小写不敏感（跟 type 一样）
            string? sensitivity = animation?.Sensitivity;
            if (!string.IsNullOrWhiteSpace(sensitivity) && !ValidSensitivities.Contains(sensitivity.ToLowerInvariant()))
            {
                errors.Add($"\"{fieldPrefix}.sensitivity\" 填的是 \"{sensitivity}\"，只能是：" + string.Join(" / ", ValidSensitivities) + "（不填就是 medium）。");
            }
        }

        private static void ValidateColor(string? hex, string fieldName, List<string> errors, bool allowEmpty = false)
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                if (!allowEmpty) errors.Add($"\"{fieldName}\" 没填。");
                return;
            }
            if (!TryParseHexColor(hex, out _))
            {
                errors.Add($"\"{fieldName}\" 的值 \"{hex}\" 不是合法的十六进制颜色，格式要是 #RRGGBB 或 #AARRGGBB。");
            }
        }

        // fieldPrefix 默认 "icon"（主图标），layers[i].icon 传 "layers[i]" 进来会拼成 "layers[i].icon.rows"
        // 这样的报错——同一套规则，只是报错信息里要能分清是主图标还是第几个额外层出的问题
        private static void ValidateIcon(CustomThemeIcon icon, List<string> errors, string fieldPrefix = "icon")
        {
            string rowsField = fieldPrefix == "icon" ? "icon.rows" : $"{fieldPrefix}.icon.rows";
            string framesField = fieldPrefix == "icon" ? "icon.frames" : $"{fieldPrefix}.icon.frames";
            string frameDurationField = fieldPrefix == "icon" ? "icon.frameDuration" : $"{fieldPrefix}.icon.frameDuration";
            string paletteField = fieldPrefix == "icon" ? "icon.palette" : $"{fieldPrefix}.icon.palette";

            // 有 frames 就走多帧校验，Rows 这时候不校验（渲染只认 frames，见 CustomThemeIcon.Frames 的注释）；
            // 没有 frames 就是老的单帧路径，行为跟这个字段加进来之前完全一样
            bool usingFrames = icon.Frames != null;
            List<string>? framesErrorSourceForPalette = null; // 校验通过的话，最后统一收集"用到的字符"给调色板核对用

            if (usingFrames)
            {
                ValidateFrames(icon.Frames!, framesField, errors, out var flattenedRows);
                framesErrorSourceForPalette = flattenedRows;
            }
            else
            {
                if (icon.Rows == null || icon.Rows.Count < MinIconSize || icon.Rows.Count > MaxIconSize)
                {
                    errors.Add($"\"{rowsField}\" 必须是 {MinIconSize}~{MaxIconSize} 行，现在是 {icon.Rows?.Count ?? 0} 行。");
                    return;
                }

                int width = icon.Rows[0].Length;
                if (width < MinIconSize || width > MaxIconSize)
                {
                    errors.Add($"\"{rowsField}\" 每一行长度必须是 {MinIconSize}~{MaxIconSize} 个字符，第 1 行现在是 {width} 个。不要求正方形，行数和每行宽度可以不一样，比如 8 行 x 16 列这种长条形也行。");
                    return;
                }

                for (int i = 0; i < icon.Rows.Count; i++)
                {
                    if (icon.Rows[i].Length != width)
                    {
                        errors.Add($"\"{rowsField}\" 第 {i + 1} 行长度是 {icon.Rows[i].Length}，跟第 1 行的 {width} 不一致——每一行必须一样宽，不然画出来的图会错位。");
                    }
                }

                framesErrorSourceForPalette = icon.Rows;
            }

            // frameDuration 不填就用默认值，填了必须是正数——道理跟 animation.duration 一样，
            // 0 或负数会让换帧计时器算出一个非正的 tick 间隔，行为没有意义
            if (icon.FrameDuration is double fd && fd <= 0)
            {
                errors.Add($"\"{frameDurationField}\" 填的是 {fd}，必须是大于 0 的数字（不填就用默认的 {DefaultFrameDurationSeconds} 秒）。");
            }

            // Actions：可选的额外可点击切换动作，跟 icon.frames 是同一套形状校验（ValidateFrames），
            // 用到的字符统一并进 framesErrorSourceForPalette 一起核对调色板——不用每个动作单独配一份调色板，
            // 图标只有一份 Palette，所有动作共用
            if (icon.Actions is { Count: > 0 } actions)
            {
                string actionsField = fieldPrefix == "icon" ? "icon.actions" : $"{fieldPrefix}.icon.actions";
                if (actions.Count > MaxIconActions)
                {
                    errors.Add($"\"{actionsField}\" 最多只能有 {MaxIconActions} 个，现在是 {actions.Count} 个。");
                }

                for (int i = 0; i < actions.Count; i++)
                {
                    var action = actions[i];
                    string actionFramesField = $"{actionsField}[{i}].frames";
                    string actionDurationField = $"{actionsField}[{i}].frameDuration";

                    if (action.Frames is not { Count: > 0 })
                    {
                        errors.Add($"\"{actionFramesField}\" 没填——每个动作至少要有 1 帧。");
                        continue;
                    }

                    ValidateFrames(action.Frames, actionFramesField, errors, out var actionFlattenedRows);
                    framesErrorSourceForPalette = framesErrorSourceForPalette.Concat(actionFlattenedRows).ToList();

                    if (action.FrameDuration is double afd && afd <= 0)
                    {
                        errors.Add($"\"{actionDurationField}\" 填的是 {afd}，必须是大于 0 的数字（不填就落回 \"{frameDurationField}\"，再没有就是默认的 {DefaultFrameDurationSeconds} 秒）。");
                    }

                    // 动作自己的 animation：不填就是这个字段加进来之前的行为（沿用 icon 顶层的
                    // animation）。填了的话跟 layers[i].animation 一样是一份完整独立的配置——
                    // type 必填，形状规则复用 ValidateAnimation，drift/fall 可以选（会让图标点到这个
                    // 动作时真的搬进专属的飘过/飘落轨道），但跟顶层 animation 一样不能跟别的招式组合
                    // （allowDriftFallCombo: false，跟顶层用的是同一条组合规则）。
                    if (action.Animation != null)
                    {
                        ValidateAnimation(action.Animation, $"{actionsField}[{i}].animation", errors, allowDriftFallCombo: false);
                    }

                    // 自动切换阈值：不填就是纯手动点击，填了必须是正数——道理跟 frameDuration/animation.duration
                    // 一样，0 或负数没有意义（"连续播放 0 秒就自动切"等于一开始就该是这个动作，那应该直接
                    // 把它内容画成动作 0，不需要这个字段）
                    if (action.AutoSwitchAfterSeconds is double asas && asas <= 0)
                    {
                        errors.Add($"\"{actionsField}[{i}].autoSwitchAfterSeconds\" 填的是 {asas}，必须是大于 0 的数字（不填就是纯手动点击切换，不会自动触发）。");
                    }
                }
            }

            // "icon.palette" 必须存在——就算图标全是 "." 空白格用不上任何颜色，也留一个空对象 {}。
            // 之前这里漏填会直接放行（下面 usedChars 为空就不会报错），结果渲染那边对 Palette 做 .ToDictionary(...)
            // 直接空引用崩掉；现在强制要求这个字段一定存在，从源头堵死这个情况。
            if (icon.Palette == null)
            {
                errors.Add($"\"{paletteField}\" 没填——就算图标不需要颜色，也要留一个空对象 {{}}。");
                return;
            }

            // 键必须正好 1 个字符，不然渲染那边取 key[0] 会因为空字符串直接抛异常崩溃
            foreach (var key in icon.Palette.Keys)
            {
                if (key.Length != 1)
                {
                    errors.Add($"\"{paletteField}\" 里的键 \"{key}\" 必须正好是 1 个字符（不能是空字符串或者多个字符）。");
                }
            }

            // framesErrorSourceForPalette 在校验失败（比如某帧行宽不齐）时可能包含脏数据，但这里只是拿来
            // 收集"用到了哪些字符"，脏数据顶多让调色板报错报得更多一点，不会崩——上面那些结构性错误
            // 已经各自 return/记录过了，这里不用再重复判断一遍"是不是已经出过错"
            var usedChars = framesErrorSourceForPalette.SelectMany(r => r).Distinct().Where(c => c != '.');
            foreach (var c in usedChars)
            {
                string key = c.ToString();
                if (!icon.Palette.TryGetValue(key, out var hex))
                {
                    errors.Add($"\"{(usingFrames ? framesField : rowsField)}\" 里用了字符 '{c}'，但 \"{paletteField}\" 里没有给它配颜色。");
                }
                else if (!TryParseHexColor(hex, out _))
                {
                    errors.Add($"\"{paletteField}\" 里 '{c}' 对应的颜色 \"{hex}\" 不是合法的十六进制颜色。");
                }
            }
        }

        /// <summary>逐帧校验：每一帧先各自检查形状合不合规（复用跟单帧 Rows 一样的行数/宽度范围），
        /// 再统一检查所有帧是不是彼此同宽同高——顺序很重要，先把"这一帧本身就不合规"挑出来单独报错，
        /// 不然"尺寸跟第一帧不一致"的报错会把注意力从真正的格式错误上带偏。帧数不设上限，用户想画
        /// 几帧就几帧，见 CustomThemeIcon.Frames 的注释。flattenedRows 吐出所有帧摊平之后的行，
        /// 给调用方统一核对调色板用，不用再重新遍历一次 Frames。</summary>
        private static void ValidateFrames(List<List<string>> frames, string framesField, List<string> errors, out List<string> flattenedRows)
        {
            flattenedRows = new List<string>();

            if (frames.Count == 0)
            {
                errors.Add($"\"{framesField}\" 给了这个字段就至少要有 1 帧，不能是空数组——真的只想要 1 帧的话直接用 \"rows\" 就够了，不用特地包一层 frames。");
                return;
            }

            int? firstWidth = null, firstHeight = null;
            for (int f = 0; f < frames.Count; f++)
            {
                var rows = frames[f];
                string prefix = $"{framesField}[{f}]";

                if (rows == null || rows.Count < MinIconSize || rows.Count > MaxIconSize)
                {
                    errors.Add($"\"{prefix}\" 必须是 {MinIconSize}~{MaxIconSize} 行，现在是 {rows?.Count ?? 0} 行。");
                    continue;
                }

                int width = rows[0].Length;
                if (width < MinIconSize || width > MaxIconSize)
                {
                    errors.Add($"\"{prefix}\" 每一行长度必须是 {MinIconSize}~{MaxIconSize} 个字符，第 1 行现在是 {width} 个。");
                    continue;
                }

                bool rowWidthOk = true;
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].Length != width)
                    {
                        errors.Add($"\"{prefix}\" 第 {i + 1} 行长度是 {rows[i].Length}，跟第 1 行的 {width} 不一致——每一行必须一样宽。");
                        rowWidthOk = false;
                    }
                }
                if (!rowWidthOk) continue;

                flattenedRows.AddRange(rows);

                firstWidth ??= width;
                firstHeight ??= rows.Count;
                if (width != firstWidth || rows.Count != firstHeight)
                {
                    errors.Add($"\"{prefix}\" 是 {rows.Count} 行 x {width} 列，跟第 1 帧的 {firstHeight} 行 x {firstWidth} 列不一样——所有帧必须是同一个尺寸，不然切换的时候图标会跳着缩放/错位，看着是「抖」不是「动」。");
                }
            }
        }

        /// <summary>把 icon.palette（单字符 -> 十六进制颜色字符串）转成一份 Dictionary&lt;char, RgbaColor&gt;，
        /// "." 没显式配色的话补一个透明——渲染现场（WPF 那边是 MainWindow.Skins.cs / CustomThemeWindow.
        /// xaml.cs，经 CustomThemeColorInterop 转成 System.Windows.Media.Color 再喂给 PixelArt.Build；
        /// Android 端以后会有自己的转换）都要做这同一步转换，抽出来共用一份，不然各平台各写一遍，
        /// 以后调色逻辑（比如要支持简写的 3 位十六进制）改了容易漏改。
        /// 调用前应该已经过 ValidateIcon 校验，这里不重复校验，纯粹是格式转换。
        /// 渲染成实际位图（BuildCustomIconFrames）不在这里——那是画位图的动作，是 UI 渲染而不是数据转换，
        /// 各平台自己的图形 API 不一样（WPF 是 BitmapSource，Android 会是别的东西），见 WPF 那边的
        /// CustomThemeColorInterop.BuildCustomIconFrames。</summary>
        public static Dictionary<char, RgbaColor> BuildIconPalette(CustomThemeIcon icon)
        {
            var palette = icon.Palette!.ToDictionary(
                kv => kv.Key[0],
                kv => { TryParseHexColor(kv.Value, out var c); return c; });
            if (!palette.ContainsKey('.')) palette['.'] = RgbaColor.Transparent;
            return palette;
        }

        /// <summary>frameDuration 没填就用默认值——校验已经保证填了的话一定是正数，这里不重复校验。</summary>
        public static double GetFrameDurationSeconds(CustomThemeIcon icon) =>
            icon.FrameDuration is double d && d > 0 ? d : DefaultFrameDurationSeconds;

        // low/high 相对 1.0（medium）不是对称的——1.6 比 1/0.6≈1.67 略保守一点，是刻意的：
        // "反应更明显"这个方向观感上比"更克制"更容易一不小心就晃得太夸张，high 稍微收着点选
        private const double LowSensitivityMultiplier = 0.6;
        private const double MediumSensitivityMultiplier = 1.0;
        private const double HighSensitivityMultiplier = 1.6;

        /// <summary>把 animation.sensitivity 的字符串档位换算成 UpdateMusicReactiveSkin 用的倍率——
        /// 没填/填了不认识的值（理论上校验已经拦过，这里再兜底）都当 medium=1.0，也就是这个字段
        /// 加进来之前唯一的行为，保证老主题不用改就是原来的速度感。</summary>
        public static double SensitivityToMultiplier(string? sensitivity) => sensitivity?.ToLowerInvariant() switch
        {
            "low" => LowSensitivityMultiplier,
            "high" => HighSensitivityMultiplier,
            _ => MediumSensitivityMultiplier,
        };

        /// <summary>#RRGGBB 或 #AARRGGBB，缺 alpha 就当完全不透明。公开给渲染那边直接复用，不用再解析一遍。</summary>
        public static bool TryParseHexColor(string hex, out RgbaColor color)
        {
            color = RgbaColor.Transparent;
            if (string.IsNullOrWhiteSpace(hex)) return false;

            string s = hex.Trim().TrimStart('#');
            try
            {
                if (s.Length == 6)
                {
                    byte r = Convert.ToByte(s.Substring(0, 2), 16);
                    byte g = Convert.ToByte(s.Substring(2, 2), 16);
                    byte b = Convert.ToByte(s.Substring(4, 2), 16);
                    color = new RgbaColor(255, r, g, b);
                    return true;
                }
                if (s.Length == 8)
                {
                    byte a = Convert.ToByte(s.Substring(0, 2), 16);
                    byte r = Convert.ToByte(s.Substring(2, 2), 16);
                    byte g = Convert.ToByte(s.Substring(4, 2), 16);
                    byte b = Convert.ToByte(s.Substring(6, 2), 16);
                    color = new RgbaColor(a, r, g, b);
                    return true;
                }
            }
            catch (FormatException)
            {
                return false;
            }
            return false;
        }
    }
}
