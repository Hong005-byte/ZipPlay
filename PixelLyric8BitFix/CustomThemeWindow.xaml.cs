using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 自定义主题的管理页：展示怎么写（带一份完整示例）、粘贴/编辑 JSON、详细校验错误、整体实时预览、
    /// 管理最多 10 个已存的客制化主题（CustomThemeStore.MaxThemes）。跟启动设置页是分开的独立窗口，符合"客制化要开在一个新页面"的要求。
    /// </summary>
    public partial class CustomThemeWindow : Window
    {
        // 正在编辑哪一个已有主题的文件名；null 表示这次保存是"新建"，会走 5 个上限的检查
        private string? _editingFileName;

        // "↩️ 撤销上一步"背后的小历史栈——只在几个会整段替换 TxtInput.Text 的起草动作（随机生成/混搭/
        // 画板插入/导入文件/导入分享码/切去编辑另一份已存主题）之前记一笔，不是每敲一个字符都记
        // （逐字符撤销本来就是 TextBox 自带的 Ctrl+Z，不用重复造）。最多存 20 步，超过就把最早的挤掉，
        // 没人会真的连点 20 次起草按钮还想一路撤销回最开始。
        //
        // 每一步连 _editingFileName（+ 对应的提示文案/可见性）一起记，不是只记文本——之前只记文本
        // 那版有个真实的坏 bug：🎲 随机生成 / 🖌️ 画板插入这两个动作故意不清空 _editingFileName（还在
        // 编辑原来那份已存主题，见 BtnRandomize_Click 的注释），但撤销回去的时候却无脑把 _editingFileName
        // 清成了 null——等于"编辑一个已存主题 -> 随机看看 -> 不满意点撤销 -> 保存"这条路会把撤销之后
        // 那份其实没变的内容当成"新建"存成一份重复的主题，而不是覆盖更新原来那份，用户会觉得"怎么保存
        // 保存不到原来那份"。现在把 _editingFileName 也存进历史，撤销真的是把"当时的状态"原样恢复。
        private const int MaxHistorySteps = 20;
        private sealed record HistorySnapshot(string Text, string? EditingFileName, string EditingHintText, Visibility EditingHintVisibility);
        private readonly List<HistorySnapshot> _history = new();

        // 主图标逐帧动画（theme.icon.frames）的预览——跟正式渲染（MainWindow.Skins.cs）不是同一套计时器：
        // 那边复用了本来就存在的 50ms 主循环（SmoothTimer_Tick），这个窗口没有那样一个常驻循环，
        // 单独开一个按 frameDuration 直接定间隔的计时器更简单，不用再算"多少个 50ms tick 凑够一帧"。
        // 也没有音乐律动那层（编辑页没有音频采集）——固定按 frameDuration 播，跟 8 招式预览是同一个简化程度。
        //
        // 用字段初始化器而不是在构造函数体里赋值——构造函数体里 InitializeComponent() 之后紧接着就调了
        // UpdatePreview()，那条路径（不管走 ApplyPreviewTheme 还是 ClearPreview）一定会摸到
        // ResetPreviewIconAnimationState/ClearPreview 里的 _previewFrameTimer.Stop()。之前这个字段是在
        // UpdatePreview() 调用之后才在构造函数体里 new 出来的，第一次打开这个窗口、TxtInput 还是空的时候
        // 就会走到 ClearPreview() 摸一个还是 null 的 _previewFrameTimer，直接 NullReferenceException——
        // 每次打开自定义主题页都会崩。字段初始化器在 InitializeComponent() 之前就跑完了，不会有这个先后顺序问题。
        private readonly System.Windows.Threading.DispatcherTimer _previewFrameTimer = new();
        private BitmapSource[]? _previewIconFrames;
        private int _previewIconFrameIndex;

        private void PushHistory()
        {
            _history.Add(new HistorySnapshot(TxtInput.Text, _editingFileName, TxtEditingHint.Text, TxtEditingHint.Visibility));
            if (_history.Count > MaxHistorySteps) _history.RemoveAt(0);
            BtnUndo.IsEnabled = true;
        }

        private void BtnUndo_Click(object sender, RoutedEventArgs e)
        {
            if (_history.Count == 0) return;
            HideMessages();

            var previous = _history[^1];
            _history.RemoveAt(_history.Count - 1);
            BtnUndo.IsEnabled = _history.Count > 0;

            _editingFileName = previous.EditingFileName;
            TxtEditingHint.Text = previous.EditingHintText;
            TxtEditingHint.Visibility = previous.EditingHintVisibility;
            TxtInput.Text = previous.Text; // 触发 TxtInput_TextChanged -> UpdatePreview()
        }

        /// <summary>true 表示这次窗口关闭时至少成功保存/删除过一次，调用方（设置页）据此决定要不要刷新主题列表。</summary>
        public bool ThemesChanged { get; private set; }

        public CustomThemeWindow()
        {
            InitializeComponent();
            _previewFrameTimer.Tick += PreviewFrameTimer_Tick;
            // DispatcherTimer 挂在 Dispatcher 上，不会因为这个窗口关掉就自动停——不手动 Stop 的话，
            // 关掉编辑页之后它还会一直在后台空转（虽然 Tick 里访问的 PreviewIcon 已经没人看得见了，
            // 不算真的坏，但纯属浪费，也留着一份不必要的引用不让这个窗口被回收）
            Closed += (s, e) => _previewFrameTimer.Stop();

            TxtExample.Text = BuildExampleJson();
            RefreshThemeList();
            UpdatePreview();
            RefreshRandomizeButtonHint();
        }

        private void PreviewFrameTimer_Tick(object? sender, EventArgs e)
        {
            if (_previewIconFrames is not { Length: > 1 } frames) return;
            _previewIconFrameIndex = (_previewIconFrameIndex + 1) % frames.Length;
            PreviewIcon.Source = frames[_previewIconFrameIndex];
            IconZoomPreview.Source = frames[_previewIconFrameIndex]; // 放大框跟主预览显示同一帧，方便确认逐帧动画顺不顺
        }

        // "主题工匠"成就解锁前后，🎲 按钮的提示语不一样——没解锁的时候顺手告诉一句"存一个就能解锁"，
        // 免得用户压根不知道这个联动的存在；解锁之后换成确认口吻。解锁状态只在打开窗口/保存完成后
        // 重新算一次（RefreshThemeList 之后调用），不用每次敲键盘都查一遍磁盘上存了几个主题。
        private void RefreshRandomizeButtonHint()
        {
            bool unlocked = CustomThemeAchievement.IsUnlocked();
            BtnRandomize.ToolTip = unlocked
                ? "已解锁「主题工匠」成就：随机池里带着一份限定配色"
                : "存过一个客制化主题就能解锁「主题工匠」成就，随机池里会多一份限定配色";
        }

        // 每次改动 JSON 就现算一遍——跟点"保存"用的是同一套 CustomThemeValidator.ParseAndValidate，
        // 只有整份 JSON 都过校验才会照着重画整个预览卡片，校验没过就留白（不在这里重复报错，具体错误还是走
        // BtnSave_Click 那条路），免得用户还没打完一个字段就被一堆"缺这个缺那个"的提示轰炸
        private void TxtInput_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

        private void UpdatePreview()
        {
            var (theme, errors) = CustomThemeValidator.ParseAndValidate(TxtInput.Text);
            if (theme != null && errors.Count == 0)
            {
                try
                {
                    ApplyPreviewTheme(theme);
                    PreviewContent.Visibility = Visibility.Visible;
                    TxtPreviewHint.Visibility = Visibility.Collapsed;
                    return;
                }
                catch (Exception ex)
                {
                    // 校验都过了理论上不该再炸，真出意外也只是预览留空，不影响正常编辑/保存，记一笔方便回头查
                    AppLog.Error("CustomThemeWindow.UpdatePreview", ex);
                }
            }

            ClearPreview();
        }

        // 把校验通过的 CustomTheme 整套套到预览卡片上：背景/标题/歌手名/歌词框/图标动画，
        // 跟 MainWindow.Skins.cs 的 ApplyCustomSkinVisuals 是同一份数据的两套独立渲染实现（不是共享代码），
        // 具体取舍见 CustomThemeWindow.xaml 里 PreviewCard 上面那段注释
        private void ApplyPreviewTheme(CustomTheme theme)
        {
            var colors = theme.Colors!;
            CustomThemeValidator.TryParseHexColor(colors.Title!, out var title);
            CustomThemeValidator.TryParseHexColor(colors.Artist!, out var artist);
            CustomThemeValidator.TryParseHexColor(colors.Accent!, out var accent);
            CustomThemeValidator.TryParseHexColor(colors.Lyric!, out var lyric);
            CustomThemeValidator.TryParseHexColor(string.IsNullOrWhiteSpace(colors.Glow) ? colors.Accent! : colors.Glow, out var glow);
            CustomThemeValidator.TryParseHexColor(colors.LyricBoxBg!, out var lyricBoxBg);
            CustomThemeValidator.TryParseHexColor(colors.LyricBoxBorder!, out var lyricBoxBorder);

            var font = new FontFamily(string.IsNullOrWhiteSpace(theme.Font) ? "Segoe UI" : theme.Font);

            PreviewCard.Background = BuildBackgroundBrush(theme.Background!);

            PreviewTitle.Foreground = new SolidColorBrush(title);
            PreviewTitle.FontFamily = font;
            PreviewArtist.Foreground = new SolidColorBrush(artist);
            PreviewArtist.FontFamily = font;

            PreviewLyric.Foreground = new SolidColorBrush(lyric);
            PreviewLyric.FontFamily = font;
            PreviewLyric.Effect = colors.GlowBlur > 0
                ? new DropShadowEffect { Color = glow, BlurRadius = colors.GlowBlur, ShadowDepth = 0, Opacity = 0.9 }
                : null;

            PreviewLyricBox.Background = new SolidColorBrush(lyricBoxBg);
            PreviewLyricBox.BorderBrush = new SolidColorBrush(lyricBoxBorder);

            // frames 数组：没有 icon.frames 就是长度 1（这个字段加进来之前的唯一行为）。尺寸提示文字
            // 改成从渲染出来的位图（PixelWidth/PixelHeight）读，不是直接读 theme.Icon.Rows——Rows 在
            // 多帧模式下可能是 null（渲染只认 Frames，见 CustomThemeIcon.Frames 的注释），从位图读
            // 两种情况都对，不用分支判断
            var iconFrames = CustomThemeValidator.BuildCustomIconFrames(theme.Icon!);
            var iconBitmap = iconFrames[0];
            PreviewIcon.Source = iconBitmap;
            string iconSizeText = $"图标 {iconBitmap.PixelHeight} 行 x {iconBitmap.PixelWidth} 列" + (iconFrames.Length > 1 ? $"，共 {iconFrames.Length} 帧" : "");
            TxtIconSizeHint.Text = iconSizeText;

            // 放大版用的是同一批 iconFrames，不重新画一遍——两边看到的必须是完全同一份数据，
            // 不然万一哪天两处渲染逻辑走岔了，两个预览显示的图标对不上，反而更容易让人怀疑是不是哪里出错了
            IconZoomPreview.Source = iconBitmap;
            TxtIconZoomHint.Visibility = Visibility.Collapsed;
            TxtIconZoomSizeHint.Text = iconSizeText;

            // type 可能是 "pulse+sway" 这种组合——预览这边没有主图标 drift/fall 专属轨道那个结构性限制
            // （单图标 + 变换组，8 招怎么组合都是同一套渲染），但校验已经把主图标的 drift/fall 组合挡掉了，
            // 这里不会真的收到那种非法组合
            ResetPreviewIconAnimationState(accent);
            foreach (var t in CustomThemeValidator.SplitAnimationTypes(theme.Animation!.Type!))
            {
                StartPreviewIconAnimation(t, theme.Animation.Duration);
            }
            ApplyPreviewLayers(theme, accent);

            // 逐帧动画只在真的 >1 帧时才启动计时器——1 帧（或者没填 frames）就跟这个功能加进来之前
            // 完全一样，PreviewIcon/IconZoomPreview 保持 ResetPreviewIconAnimationState 里已经清空的状态，
            // 显示上面已经设好的这张静态 iconBitmap，不会多出一个不必要的计时器在后台跑
            if (iconFrames.Length > 1)
            {
                _previewIconFrames = iconFrames;
                _previewIconFrameIndex = 0;
                _previewFrameTimer.Interval = TimeSpan.FromSeconds(CustomThemeValidator.GetFrameDurationSeconds(theme.Icon!));
                _previewFrameTimer.Start();
            }
        }

        // theme.layers 的预览版：跟 MainWindow.Skins.cs 的 ApplyCustomExtraLayers 是同一个思路
        // （动态创建 Image + 四角定位 + 复用 8 招式动画），预览页这边同样没有音乐律动这层，
        // 动画照样播、只是不会跟着音乐变速——跟主图标预览的简化程度保持一致
        private void ApplyPreviewLayers(CustomTheme theme, Color accent)
        {
            PreviewLayersHost.Children.Clear();
            if (theme.Layers == null) return;

            foreach (var layer in theme.Layers)
            {
                // 层目前不支持逐帧动画（见 CustomTheme.cs 里 icon.frames 的范围说明），但校验没有单独
                // 挡住"层的 icon 里写了 frames"这种情况（ValidateIcon 是主图标/层共用的同一套规则）——
                // 用 BuildCustomIconFrames 取第一帧当静态图，跟主图标之外那几处（Mini 小方块/drift/fall/
                // 分享卡片）是同一个退化策略，不会因为用户在层里写了 frames 就直接崩
                var bitmap = CustomThemeValidator.BuildCustomIconFrames(layer.Icon!)[0];

                var rotate = new RotateTransform();
                var translate = new TranslateTransform();
                var glow = new DropShadowEffect { Color = accent, ShadowDepth = 0, BlurRadius = 8, Opacity = 0.6 };

                var image = new Image
                {
                    Width = 22,
                    Height = 22,
                    Stretch = Stretch.Uniform,
                    Source = bitmap,
                    RenderTransform = new TransformGroup { Children = { rotate, translate } },
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    Effect = glow,
                    Margin = new Thickness(8),
                };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                switch (layer.Anchor?.ToLowerInvariant())
                {
                    case "top-left":
                        image.HorizontalAlignment = HorizontalAlignment.Left;
                        image.VerticalAlignment = VerticalAlignment.Top;
                        break;
                    case "top-right":
                        image.HorizontalAlignment = HorizontalAlignment.Right;
                        image.VerticalAlignment = VerticalAlignment.Top;
                        break;
                    case "bottom-left":
                        image.HorizontalAlignment = HorizontalAlignment.Left;
                        image.VerticalAlignment = VerticalAlignment.Bottom;
                        break;
                    default:
                        image.HorizontalAlignment = HorizontalAlignment.Right;
                        image.VerticalAlignment = VerticalAlignment.Bottom;
                        break;
                }
                PreviewLayersHost.Children.Add(image);

                foreach (var t in CustomThemeValidator.SplitAnimationTypes(layer.Animation!.Type!))
                {
                    StartPreviewLayerAnimation(t, layer.Animation.Duration, image, rotate, translate, glow);
                }
            }
        }

        // 跟 StartPreviewIconAnimation 同一套 8 招式参数，只是作用目标从固定命名元素换成动态传进来的实例；
        // drift/fall 在这里也没有"飘过/飘落整张卡片"的专属轨道，退化成原地小幅摆动，跟 MainWindow.Skins.cs
        // 的 StartLayerAnimation 是同一个取舍（那边的注释有完整说明，这里不重复）
        private static void StartPreviewLayerAnimation(string type, double? customDuration, Image icon, RotateTransform rotate, TranslateTransform translate, DropShadowEffect glow)
        {
            switch (type)
            {
                case "pulse":
                    glow.BeginAnimation(DropShadowEffect.OpacityProperty,
                        new DoubleAnimation(0.35, 0.75, TimeSpan.FromSeconds(SafeDuration(customDuration, 2.2)))
                        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                    break;
                case "twinkle":
                    icon.BeginAnimation(UIElement.OpacityProperty,
                        new DoubleAnimation(0.25, 1.0, TimeSpan.FromSeconds(SafeDuration(customDuration, 1.6)))
                        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                    break;
                case "sway":
                    rotate.BeginAnimation(RotateTransform.AngleProperty,
                        new DoubleAnimation(-8, 8, TimeSpan.FromSeconds(SafeDuration(customDuration, 3.2)))
                        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
                    break;
                case "spin":
                    rotate.BeginAnimation(RotateTransform.AngleProperty,
                        new DoubleAnimation(0, 360, TimeSpan.FromSeconds(SafeDuration(customDuration, 4)))
                        { RepeatBehavior = RepeatBehavior.Forever });
                    break;
                case "flicker":
                    {
                        double dur = SafeDuration(customDuration, 2.0);
                        var frames = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
                        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.5, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.75, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(dur * 0.15))));
                        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.4, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(dur * 0.3))));
                        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.7, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(dur * 0.42))));
                        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.5, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(dur))));
                        glow.BeginAnimation(DropShadowEffect.OpacityProperty, frames);
                        break;
                    }
                case "drift":
                    translate.BeginAnimation(TranslateTransform.XProperty,
                        new DoubleAnimation(-10, 10, TimeSpan.FromSeconds(SafeDuration(customDuration, 4)))
                        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
                    break;
                case "fall":
                    translate.BeginAnimation(TranslateTransform.YProperty,
                        new DoubleAnimation(-10, 10, TimeSpan.FromSeconds(SafeDuration(customDuration, 4)))
                        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
                    break;
                case "bob":
                    translate.BeginAnimation(TranslateTransform.YProperty,
                        new DoubleAnimation(-6, 6, TimeSpan.FromSeconds(SafeDuration(customDuration, 3)))
                        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
                    break;
            }
        }

        // 背景刷子：纯色 solid 或 2~4 色渐变 gradient，跟 MainWindow.Skins.cs 的 ApplyCustomSkinVisuals
        // 里那段是同一套逻辑（方向 vertical/diagonal、渐变站点等分布），复制过来是因为那边是私有实例方法，
        // 抽公共方法要跨两个窗口共享意义不大——这几行逻辑本身很稳定，不太会出现"改一边忘了改另一边"的情况
        private static Brush BuildBackgroundBrush(CustomThemeBackground bg)
        {
            var stops = (bg.Stops ?? new System.Collections.Generic.List<string>())
                .Select(s => { CustomThemeValidator.TryParseHexColor(s, out var c); return c; })
                .ToList();

            if (string.Equals(bg.Type, "gradient", StringComparison.OrdinalIgnoreCase) && stops.Count >= 2)
            {
                var gradient = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = string.Equals(bg.Direction, "diagonal", StringComparison.OrdinalIgnoreCase)
                        ? new Point(1, 1) : new Point(0, 1),
                };
                for (int i = 0; i < stops.Count; i++)
                    gradient.GradientStops.Add(new GradientStop(stops[i], stops.Count == 1 ? 0 : (double)i / (stops.Count - 1)));
                return gradient;
            }
            return new SolidColorBrush(stops.Count > 0 ? stops[0] : Colors.Black);
        }

        // 先清掉上一次挂的动画、回到基准姿态，再决定这次播哪(几)招——每敲一个字都会重新调用这一整套，
        // 不重置的话，比如从 spin 切成 pulse，图标可能还停在上一次转到一半的角度上；组合多招的情况下
        // 这个方法只在循环开始前调一次，不要挪进 StartPreviewIconAnimation 里，不然后一招重置的时候
        // 会把前一招刚设好的状态擦掉
        private void ResetPreviewIconAnimationState(Color accent)
        {
            // 逐帧动画状态也在这里一起重置——每次 ApplyPreviewTheme 都会先调这个方法再决定要不要重新
            // 启动计时器，不重置的话，从"带 frames 的主题"改成"不带 frames 的主题"时，旧的计时器还会
            // 继续跑、拿着上一份主题的位图数组往 PreviewIcon 上贴，跟这次新的静态图打架
            _previewFrameTimer.Stop();
            _previewIconFrames = null;

            PreviewIconRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            PreviewIconRotate.Angle = 0;
            PreviewIconTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            PreviewIconTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            PreviewIconTranslate.X = 0;
            PreviewIconTranslate.Y = 0;
            PreviewIcon.BeginAnimation(UIElement.OpacityProperty, null);
            PreviewIcon.Opacity = 1;
            PreviewIconGlow.Color = accent;
            PreviewIconGlow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
            PreviewIconGlow.Opacity = 0.6;
        }

        // 8 种招式的预览版（单招或者用 + 组合几个）：参数（幅度/默认时长）照抄 MainWindow.Skins.cs 的
        // StartCustomIconAnimation，保证"预览里看着多快"跟"保存后套到播放器里多快"是一致的观感。
        // 跟正式版的差别只有两处——这里没有音乐律动调速（编辑页没有音频采集，开不开 musicReactive
        // 动画照样按固定节奏播），drift/fall 用同一个图标在卡片范围内来回飘/落，不是正式版那种三图标
        // 交错起播的效果。
        private void StartPreviewIconAnimation(string type, double? customDuration)
        {
            switch (type)
            {
                case "pulse":
                    PreviewIconGlow.BeginAnimation(DropShadowEffect.OpacityProperty,
                        new DoubleAnimation(0.35, 0.75, TimeSpan.FromSeconds(SafeDuration(customDuration, 2.2)))
                        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                    break;
                case "twinkle":
                    PreviewIcon.BeginAnimation(UIElement.OpacityProperty,
                        new DoubleAnimation(0.25, 1.0, TimeSpan.FromSeconds(SafeDuration(customDuration, 1.6)))
                        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                    break;
                case "sway":
                    PreviewIconRotate.BeginAnimation(RotateTransform.AngleProperty,
                        new DoubleAnimation(-8, 8, TimeSpan.FromSeconds(SafeDuration(customDuration, 3.2)))
                        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
                    break;
                case "spin":
                    PreviewIconRotate.BeginAnimation(RotateTransform.AngleProperty,
                        new DoubleAnimation(0, 360, TimeSpan.FromSeconds(SafeDuration(customDuration, 4)))
                        { RepeatBehavior = RepeatBehavior.Forever });
                    break;
                case "flicker":
                    {
                        double dur = SafeDuration(customDuration, 2.0);
                        var frames = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
                        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.5, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.75, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(dur * 0.15))));
                        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.4, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(dur * 0.3))));
                        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.7, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(dur * 0.42))));
                        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.5, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(dur))));
                        PreviewIconGlow.BeginAnimation(DropShadowEffect.OpacityProperty, frames);
                        break;
                    }
                case "bob":
                    PreviewIconTranslate.BeginAnimation(TranslateTransform.YProperty,
                        new DoubleAnimation(-5, 5, TimeSpan.FromSeconds(SafeDuration(customDuration, 3)))
                        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
                    break;
                case "drift":
                    PreviewIconTranslate.BeginAnimation(TranslateTransform.XProperty,
                        new DoubleAnimation(-20, 360, TimeSpan.FromSeconds(SafeDuration(customDuration, 14)))
                        { RepeatBehavior = RepeatBehavior.Forever });
                    break;
                case "fall":
                    PreviewIconTranslate.BeginAnimation(TranslateTransform.YProperty,
                        new DoubleAnimation(-20, 120, TimeSpan.FromSeconds(SafeDuration(customDuration, 6)))
                        { RepeatBehavior = RepeatBehavior.Forever });
                    break;
            }
        }

        // CustomThemeValidator 已经要求 duration 必须 > 0，这里再兜底一层——跟 MainWindow.Skins.cs 里
        // 同名方法一样的道理，防的是"校验之前存的老文件"或者校验逻辑本身以后有疏漏
        private static double SafeDuration(double? value, double fallback) =>
            value.HasValue && value.Value > 0 ? value.Value : fallback;

        private void ClearPreview()
        {
            _previewFrameTimer.Stop();
            _previewIconFrames = null;

            PreviewIconRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            PreviewIconTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            PreviewIconTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            PreviewIcon.BeginAnimation(UIElement.OpacityProperty, null);
            PreviewIconGlow.BeginAnimation(DropShadowEffect.OpacityProperty, null);

            // 校验没过整块 PreviewContent（背景/标题/歌手名/歌词框/图标）直接收起来，不逐个清画刷——
            // 不然上一次校验通过时设过的文字颜色/歌词框边框颜色还留着原值，只是背景变透明，中间会露出
            // 上一份主题的文字/边框残影，看着像"半个主题叠在另一个主题上"，不是真的"留空"
            PreviewCard.Background = new SolidColorBrush(Color.FromRgb(0x12, 0x16, 0x0F));
            PreviewContent.Visibility = Visibility.Collapsed;
            PreviewLayersHost.Children.Clear();
            TxtPreviewHint.Visibility = Visibility.Visible;

            IconZoomPreview.Source = null;
            TxtIconZoomHint.Visibility = Visibility.Visible;
            TxtIconZoomSizeHint.Text = "";
        }

        // 示例 JSON 抽到了 CustomThemeExample 里共用（CustomThemeSpecDoc 那份下载文档末尾用的是
        // 同一份文本），这里留一个同名方法只是不想动构造函数那行调用点
        private static string BuildExampleJson() => CustomThemeExample.Json;

        private void BtnCopyExample_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(TxtExample.Text); } catch { /* 剪贴板偶尔会被别的程序占用，不是关键功能，失败就算了 */ }
        }

        // 完整字段说明存成 .md 文件让用户自己选地方存——结构化的标题/表格，人愿意仔细看的话比页面里
        // 那几条要点信息量大得多，也方便直接整份丢给 AI 当"生成规则"用（见 CustomThemeSpecDoc 的说明）
        private void BtnDownloadSpec_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "下载自定义主题详细说明",
                Filter = "Markdown 文件 (*.md)|*.md|文本文件 (*.txt)|*.txt",
                FileName = "ZipPlay-自定义主题说明.md",
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                File.WriteAllText(dialog.FileName, CustomThemeSpecDoc.Build());
            }
            catch (Exception ex)
            {
                AppLog.Error("CustomThemeWindow.BtnDownloadSpec_Click", ex);
                ShowErrors(new[] { "文档保存失败（可能是磁盘空间或权限问题），详情已经记到日志文件里了。" });
            }
        }

        // 抽一份配好色的草稿直接塞进输入框——设置 Text 会触发 TxtInput_TextChanged，预览卡片跟着立刻更新，
        // 不用额外调用。故意不清空"正在编辑：xxx"的提示：多点几下随机只是在改输入框里的草稿内容，
        // 不代表用户放弃了正在编辑的那个已存主题，真正决定"这次保存是新建还是覆盖"的是 _editingFileName，
        // 这里不去动它——点了保存的话照样会覆盖那份正在编辑的主题，这个提示应该继续挂着提醒用户
        // Common 不弹提示（那是最常见的结果，每次都念叨反而烦）；Rare 以上弹一句"出货"反馈，
        // 稀有度越高颜色越显眼——纯粹是给"抽卡"这个动作加点游戏感，跟主题本身能不能存、
        // 能不能过校验完全没关系，Rare/Epic/Limited 生成出来的 JSON 结构跟 Common 一模一样。
        private void BtnRandomize_Click(object sender, RoutedEventArgs e)
        {
            HideMessages();
            PushHistory();
            var result = CustomThemeRandomizer.GenerateWithRarity(CustomThemeAchievement.IsUnlocked());
            TxtInput.Text = result.Json;

            (string label, string color) = result.Rarity switch
            {
                PaletteRarity.Limited => ("🌟 限定", "#D4AF37"),
                PaletteRarity.Epic => ("💜 史诗", "#E040FB"),
                PaletteRarity.Rare => ("✨ 稀有", "#4FD1C5"),
                _ => ("", ""),
            };
            if (label.Length > 0)
            {
                var brush = (Brush)new BrushConverter().ConvertFromString(color)!;
                ShowSuccess($"{label}配色！抽到了「{result.Mood}」", brush);
            }
        }

        // 从别人分享的 .json 文件导入——读文件内容塞进输入框，不直接落地。是不是真的存、
        // 会不会撞到 5 个上限，都还是走"保存"按钮已有的 CustomThemeValidator/CustomThemeStore 那条路，
        // 这里只负责"把文件内容念出来"这一步，不重复校验/存储逻辑
        private void BtnImportTheme_Click(object sender, RoutedEventArgs e)
        {
            HideMessages();

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "导入自定义主题",
                Filter = "JSON 文件 (*.json)|*.json",
            };
            if (dialog.ShowDialog(this) != true) return;

            string text;
            try
            {
                text = File.ReadAllText(dialog.FileName);
            }
            catch (Exception ex)
            {
                AppLog.Error("CustomThemeWindow.BtnImportTheme_Click", ex);
                ShowErrors(new[] { "这个文件读不出来（可能被占用或者没有权限），换一个试试。" });
                return;
            }

            PushHistory();
            TxtInput.Text = text; // 触发 TxtInput_TextChanged -> UpdatePreview()，读出来是不是合法主题立刻看得出来

            // 清掉"正在编辑：xxx"的提示——导入的很可能是完全不相关的另一份主题，留着这个提示的话，
            // 用户容易没注意到还在editing一份旧主题，点保存的时候把手头正在编辑的那份意外覆盖掉
            _editingFileName = null;
            TxtEditingHint.Visibility = Visibility.Collapsed;
        }

        // 从剪贴板读一段分享码（ZPT1: 开头的 Base64 文本）解回 JSON 塞进输入框——跟"从文件导入"
        // 走的是同一条后续路径（校验/保存都还是靠输入框里现在这份内容），这里只负责"分享码怎么变回文本"。
        private void BtnImportShareCode_Click(object sender, RoutedEventArgs e)
        {
            HideMessages();

            string clipboard;
            try { clipboard = Clipboard.GetText(); }
            catch { clipboard = ""; }

            if (!CustomThemeShareCode.TryDecode(clipboard, out string json))
            {
                ShowErrors(new[] { "剪贴板里没找到有效的分享码（应该是一段 ZPT1: 开头的文字），先复制一份分享码再点这个。" });
                return;
            }

            PushHistory();
            TxtInput.Text = json; // 触发 TxtInput_TextChanged -> UpdatePreview()
            _editingFileName = null; // 分享码大概率是别人的主题，跟导入文件同理，不该被当成"接着编辑手头这份"
            TxtEditingHint.Visibility = Visibility.Collapsed;
        }

        // 打开混搭选择窗口，拿到结果就塞进输入框——跟导入/随机生成同一个套路：只改输入框，
        // 不直接落盘，_editingFileName 也一并清掉（原因跟 BtnImportTheme_Click 一样：混搭出来的
        // 大概率是一份新东西，不该被理解成"接着编辑刚才那份"）。按钮本身在主题数 < 2 时是禁用状态
        // （见 RefreshThemeList），这里不重复判断。
        private void BtnRemix_Click(object sender, RoutedEventArgs e)
        {
            HideMessages();

            var picker = new ThemeRemixWindow { Owner = this };
            if (picker.ShowDialog() != true || picker.ResultJson == null) return;

            bool remixMasterUnlockedBefore = CustomThemeAchievement.IsRemixMasterUnlocked();

            PushHistory();
            TxtInput.Text = picker.ResultJson;
            _editingFileName = null;
            TxtEditingHint.Visibility = Visibility.Collapsed;
            CustomThemeFeatureUsage.MarkRemixUsed(); // 真的拿到一份混搭草稿才算用过，见该方法注释

            // 只在真的刚解锁那一次弹一句提示——平时点混搭不用每次都刷一条"已经生成"的成功消息，
            // 草稿直接进输入框、预览立刻跟着重画就是最直接的反馈了，不需要额外的文字确认
            if (!remixMasterUnlockedBefore && CustomThemeAchievement.IsRemixMasterUnlocked())
            {
                ShowSuccess("🎉 顺带解锁「混音师」成就，成就墙里能看到。");
            }
        }

        // 点格子画图标——打开前先看当前输入框里有没有一个能续画的 icon（TryExtractIcon 解析不出来就是
        // null，画板会从空白 8x8 开始，不弹错误）。画完拿到 ResultIcon 之后尝试"智能插入"：解析当前
        // 输入框为通用 JObject、替换/新增 icon 字段、写回去，保留用户其它字段没动过——这条路失败
        // （比如输入框现在压根不是合法 JSON，或者是空的）就退化成复制到剪贴板，让用户自己粘。
        private void BtnPaintIcon_Click(object sender, RoutedEventArgs e)
        {
            HideMessages();

            var existingIcon = PixelIconEditor.TryExtractIcon(TxtInput.Text);
            var painter = new IconPainterWindow(existingIcon) { Owner = this };
            if (painter.ShowDialog() != true || painter.ResultIcon == null) return;

            bool pixelPainterUnlockedBefore = CustomThemeAchievement.IsPixelPainterUnlocked();
            CustomThemeFeatureUsage.MarkPainterUsed(); // 画完点了"插入"/"复制片段"才算用过，见该方法注释；插入进 JSON 成不成功不影响这个成就
            bool justUnlockedPixelPainter = !pixelPainterUnlockedBefore && CustomThemeAchievement.IsPixelPainterUnlocked();

            string? merged = PixelIconEditor.TryInsertIconIntoJson(TxtInput.Text, painter.ResultIcon);
            if (merged != null)
            {
                PushHistory();
                TxtInput.Text = merged;
                ShowSuccess(justUnlockedPixelPainter
                    ? "✅ 图标已经画好，替换进当前 JSON 的 icon 字段了。🎉 顺带解锁「像素画师」成就，成就墙里能看到。"
                    : "✅ 图标已经画好，替换进当前 JSON 的 icon 字段了。");
                return;
            }

            // 当前输入框不是合法 JSON（比如还是空的，或者正编辑到一半语法不完整）——没法做"替换字段"
            // 这种精细手术，退化成复制整份 icon 片段到剪贴板，用户自己找地方粘
            string fragment = PixelIconEditor.SerializeIconFragment(painter.ResultIcon);
            try { Clipboard.SetText(fragment); } catch { /* 剪贴板偶尔被占用，不是关键功能，失败就算了 */ }
            ShowErrors(new[] { "当前输入框不是合法 JSON，没法自动替换 icon 字段——图标已经复制到剪贴板，自己找地方粘吧。" });
        }

        // 存好的某个主题导出成独立 .json 文件——直接原样写出 LoadRawJson 读到的文本（保留用户自己的格式/缩进），
        // 不是重新序列化一遍 CustomTheme 对象，这样导出的文件跟当初保存时长得一模一样，也顺便验证了
        // "导出的文件能被上面 BtnImportTheme_Click 原样读回来"这条路径本身就是自洽的
        private void ExportTheme(CustomThemeEntry entry)
        {
            string? raw = CustomThemeStore.LoadRawJson(entry.FileName);
            if (raw == null)
            {
                ShowErrors(new[] { "这个主题的文件读不出来，可能已经被外部删掉或者改坏了。" });
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出自定义主题",
                Filter = "JSON 文件 (*.json)|*.json",
                FileName = $"ZipPlay-自定义主题-{SanitizeFileName(entry.Theme.Name)}.json",
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                File.WriteAllText(dialog.FileName, raw);
                ShowSuccess($"✅ 「{entry.Theme.Name}」已导出，把这个文件发给别人，对方在这个页面点 [📥 从文件导入] 就能用。");
            }
            catch (Exception ex)
            {
                AppLog.Error("CustomThemeWindow.ExportTheme", ex);
                ShowErrors(new[] { "导出失败（可能是磁盘空间或权限问题），详情已经记到日志文件里了。" });
            }
        }

        // 主题名字可能带斜杠/冒号这些文件名不允许的字符（毕竟是用户随便填的），存文件名之前先洗一遍，
        // 不然 SaveFileDialog 默认文件名里混进这些字符会直接报错弹不出保存框
        private static string SanitizeFileName(string? name)
        {
            string safe = name ?? "未命名";
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                safe = safe.Replace(c, '_');
            }
            return safe;
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            HideMessages();

            string json = TxtInput.Text;
            if (string.IsNullOrWhiteSpace(json))
            {
                ShowErrors(new[] { "还没粘贴任何内容。" });
                return;
            }

            var (theme, errors) = CustomThemeValidator.ParseAndValidate(json);
            if (theme == null || errors.Count > 0)
            {
                ShowErrors(errors);
                return;
            }

            // 存之前先问一次"主题工匠"/"收藏家"解不解锁——这是存主题之前唯一能拿到的"之前"状态，
            // 存完再问一次就已经变成 true 了，没法靠"存后的状态"反推"是不是刚刚才解锁的"
            bool themeMakerUnlockedBefore = CustomThemeAchievement.IsUnlocked();
            bool collectorUnlockedBefore = CustomThemeAchievement.IsCollectorUnlocked();

            var (success, error, savedFileName) = CustomThemeStore.Save(theme, _editingFileName);
            if (!success)
            {
                ShowErrors(new[] { error ?? "保存失败。" });
                return;
            }

            ThemesChanged = true;
            _editingFileName = savedFileName;
            TxtEditingHint.Text = $"正在编辑：{theme.Name}（再次保存会覆盖更新这一份，不会新建）";
            TxtEditingHint.Visibility = Visibility.Visible;

            // 一次保存理论上最多同时新解锁这两个之一（收藏家要求正好存满 10 个，主题工匠只要求存过 1 个，
            // 两个条件不会在同一次保存里同时从"没解锁"变成"解锁"——但还是拼成列表处理，不用两层嵌套三元，
            // 万一以后又加了新的自定义主题成就，这里不用再改结构
            var newlyUnlockedNotes = new List<string>();
            if (!themeMakerUnlockedBefore && CustomThemeAchievement.IsUnlocked())
                newlyUnlockedNotes.Add("「主题工匠」成就（🎲 随机生成里多了一份限定配色）");
            if (!collectorUnlockedBefore && CustomThemeAchievement.IsCollectorUnlocked())
                newlyUnlockedNotes.Add("「收藏家」成就");

            ShowSuccess(newlyUnlockedNotes.Count > 0
                ? $"✅ 「{theme.Name}」保存成功，回到设置页的皮肤选择器里就能看到了。🎉 顺带解锁{string.Join("、", newlyUnlockedNotes)}。"
                : $"✅ 「{theme.Name}」保存成功，回到设置页的皮肤选择器里就能看到了。");

            RefreshThemeList();
            RefreshRandomizeButtonHint();
        }

        private void ShowErrors(System.Collections.Generic.IEnumerable<string> errors)
        {
            ErrorPanel.Children.Clear();
            foreach (var err in errors)
            {
                ErrorPanel.Children.Add(new TextBlock
                {
                    Text = "• " + err,
                    Foreground = System.Windows.Media.Brushes.LightPink,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 2),
                });
            }
            ErrorBox.Visibility = Visibility.Visible;
        }

        private void HideMessages()
        {
            ErrorBox.Visibility = Visibility.Collapsed;
            SuccessBox.Visibility = Visibility.Collapsed;
        }

        // 默认的成功提示绿色——XAML 里 TxtSuccess 自己也是这个颜色，这里重复一份常量是因为
        // ShowSuccess 需要能"改成别的颜色（稀有度用）之后，下一次普通消息再改回来"，
        // 不重复的话没地方能问到"默认颜色到底是什么"
        private static readonly Brush DefaultSuccessBrush = (Brush)new BrushConverter().ConvertFromString("#7AE08A")!;

        // 所有"存成功了/导出了/分享码复制了/顺带解锁了什么成就"这些提示统一走这里——不然每个调用点
        // 都要记得自己把 Foreground 从上一次可能被稀有度反馈（BtnRandomize_Click）改成的紫色/金色
        // 改回默认绿色，漏改一处就会出现"明明是普通保存成功，字却是金色的"这种残留状态。
        private void ShowSuccess(string text, Brush? foreground = null)
        {
            TxtSuccess.Text = text;
            TxtSuccess.Foreground = foreground ?? DefaultSuccessBrush;
            SuccessBox.Visibility = Visibility.Visible;
        }

        private void RefreshThemeList()
        {
            ThemeListPanel.Children.Clear();
            var themes = CustomThemeStore.ListAll();

            TxtThemeCount.Text = $"已保存的客制化主题 ({themes.Count}/{CustomThemeStore.MaxThemes})";
            TxtNoThemes.Visibility = themes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // 混搭至少要 2 个来源才有意义（3 个下拉都选同一个主题的话就是纯复制，允许但没意思）——
            // 不够的话按钮直接禁用 + 一句提示，不是点了之后才在弹窗里告诉用户"你存的主题不够"
            BtnRemix.IsEnabled = themes.Count >= 2;
            BtnRemix.ToolTip = themes.Count >= 2
                ? "从已存的主题里各挑一个当配色/图标/动画的来源，拼一份新草稿"
                : $"存够 2 个主题才能混搭，现在有 {themes.Count} 个";

            foreach (var entry in themes)
            {
                var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var name = new TextBlock
                {
                    Text = entry.Theme.Name ?? "(未命名)",
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(name, 0);

                var editBtn = new Button
                {
                    Content = "✏️ 编辑",
                    Padding = new Thickness(8, 2, 8, 2),
                    FontSize = 10,
                    Margin = new Thickness(6, 0, 0, 0),
                    Background = System.Windows.Media.Brushes.Transparent,
                    Foreground = System.Windows.Media.Brushes.LightGray,
                    BorderBrush = System.Windows.Media.Brushes.Gray,
                    Cursor = System.Windows.Input.Cursors.Hand,
                };
                editBtn.Click += (s, e) =>
                {
                    HideMessages();
                    string? raw = CustomThemeStore.LoadRawJson(entry.FileName);
                    if (raw == null) return;
                    PushHistory();
                    TxtInput.Text = raw;
                    _editingFileName = entry.FileName;
                    TxtEditingHint.Text = $"正在编辑：{entry.Theme.Name}（保存会覆盖更新这一份，不会新建）";
                    TxtEditingHint.Visibility = Visibility.Visible;
                };
                Grid.SetColumn(editBtn, 1);

                var shareBtn = new Button
                {
                    Content = "🔗",
                    Padding = new Thickness(8, 2, 8, 2),
                    FontSize = 10,
                    Margin = new Thickness(6, 0, 0, 0),
                    Background = System.Windows.Media.Brushes.Transparent,
                    Foreground = System.Windows.Media.Brushes.LightGray,
                    BorderBrush = System.Windows.Media.Brushes.Gray,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "复制分享码，直接粘贴发给别人（不用发文件）",
                };
                var entryForShare = entry;
                shareBtn.Click += (s, e) =>
                {
                    HideMessages();
                    string? raw = CustomThemeStore.LoadRawJson(entryForShare.FileName);
                    if (raw == null)
                    {
                        ShowErrors(new[] { "这个主题的文件读不出来，可能已经被外部删掉或者改坏了。" });
                        return;
                    }
                    try { Clipboard.SetText(CustomThemeShareCode.Encode(raw)); } catch { /* 剪贴板偶尔被占用，不是关键功能，失败就算了 */ }
                    ShowSuccess($"✅ 「{entryForShare.Theme.Name}」的分享码已经复制到剪贴板，直接粘贴发给别人就行，对方点 [🔗 粘贴分享码导入] 就能用。");
                };
                Grid.SetColumn(shareBtn, 2);

                var exportBtn = new Button
                {
                    Content = "📤",
                    Padding = new Thickness(8, 2, 8, 2),
                    FontSize = 10,
                    Margin = new Thickness(6, 0, 0, 0),
                    Background = System.Windows.Media.Brushes.Transparent,
                    Foreground = System.Windows.Media.Brushes.LightGray,
                    BorderBrush = System.Windows.Media.Brushes.Gray,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "导出成文件，分享给别人",
                };
                var entryForExport = entry;
                exportBtn.Click += (s, e) =>
                {
                    HideMessages();
                    ExportTheme(entryForExport);
                };
                Grid.SetColumn(exportBtn, 3);

                var deleteBtn = new Button
                {
                    Content = "🗑",
                    Padding = new Thickness(8, 2, 8, 2),
                    FontSize = 10,
                    Margin = new Thickness(6, 0, 0, 0),
                    Background = System.Windows.Media.Brushes.Transparent,
                    Foreground = System.Windows.Media.Brushes.LightPink,
                    BorderBrush = System.Windows.Media.Brushes.Gray,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "删除这个主题",
                };
                string fileNameForDelete = entry.FileName;
                deleteBtn.Click += (s, e) =>
                {
                    CustomThemeStore.Delete(fileNameForDelete);
                    ThemesChanged = true;
                    if (_editingFileName == fileNameForDelete)
                    {
                        _editingFileName = null;
                        TxtEditingHint.Visibility = Visibility.Collapsed;
                    }
                    RefreshThemeList();
                    RefreshRandomizeButtonHint(); // 删到只剩 0 个的话，"主题工匠"就重新锁上了
                };
                Grid.SetColumn(deleteBtn, 4);

                row.Children.Add(name);
                row.Children.Add(editBtn);
                row.Children.Add(shareBtn);
                row.Children.Add(exportBtn);
                row.Children.Add(deleteBtn);
                ThemeListPanel.Children.Add(row);
            }
        }
    }
}
