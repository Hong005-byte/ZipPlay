using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 自定义主题的管理页：展示怎么写（带一份完整示例）、粘贴/编辑 JSON、详细校验错误、整体实时预览、
    /// 管理最多 5 个已存的客制化主题。跟启动设置页是分开的独立窗口，符合"客制化要开在一个新页面"的要求。
    /// </summary>
    public partial class CustomThemeWindow : Window
    {
        // 正在编辑哪一个已有主题的文件名；null 表示这次保存是"新建"，会走 5 个上限的检查
        private string? _editingFileName;

        /// <summary>true 表示这次窗口关闭时至少成功保存/删除过一次，调用方（设置页）据此决定要不要刷新主题列表。</summary>
        public bool ThemesChanged { get; private set; }

        public CustomThemeWindow()
        {
            InitializeComponent();
            TxtExample.Text = BuildExampleJson();
            RefreshThemeList();
            UpdatePreview();
            RefreshRandomizeButtonHint();
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

            var iconPalette = CustomThemeValidator.BuildIconPalette(theme.Icon!);
            PreviewIcon.Source = PixelArt.BuildCustomIcon(theme.Icon!.Rows!.ToArray(), iconPalette);
            TxtIconSizeHint.Text = $"图标 {theme.Icon.Rows!.Count} 行 x {theme.Icon.Rows[0].Length} 列";

            StartPreviewIconAnimation(theme.Animation!.Type!.ToLowerInvariant(), theme.Animation.Duration, accent);
            ApplyPreviewLayers(theme, accent);
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
                var palette = CustomThemeValidator.BuildIconPalette(layer.Icon!);
                var bitmap = PixelArt.BuildCustomIcon(layer.Icon!.Rows!.ToArray(), palette);

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

                StartPreviewLayerAnimation(layer.Animation!.Type!.ToLowerInvariant(), layer.Animation.Duration, image, rotate, translate, glow);
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

        // 8 种招式的预览版：参数（幅度/默认时长）照抄 MainWindow.Skins.cs 的 StartCustomIconAnimation，
        // 保证"预览里看着多快"跟"保存后套到播放器里多快"是一致的观感。跟正式版的差别只有两处——
        // 这里没有音乐律动调速（编辑页没有音频采集，开不开 musicReactive 动画照样按固定节奏播），
        // drift/fall 用同一个图标在卡片范围内来回飘/落，不是正式版那种三图标交错起播的效果。
        private void StartPreviewIconAnimation(string type, double? customDuration, Color accent)
        {
            // 先清掉上一次挂的动画、回到基准姿态，再决定这次播哪一招——每敲一个字都会重新调用这个方法，
            // 不重置的话，比如从 spin 切成 pulse，图标可能还停在上一次转到一半的角度上
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
        }

        // 拿现有的 Sunset 皮肤当例子——现成的、已经在用的配色，比瞎编一份更有说服力，也顺便验证了
        // "内置皮肤" 和 "客制化皮肤" 走的是同一套字段，不是另外发明了一套格式
        private static string BuildExampleJson() =>
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
  ""animation"": { ""type"": ""pulse"", ""duration"": 2.6, ""musicReactive"": true, ""sensitivity"": ""medium"" },
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

        private void BtnCopyExample_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(TxtExample.Text); } catch { /* 剪贴板偶尔会被别的程序占用，不是关键功能，失败就算了 */ }
        }

        // 抽一份配好色的草稿直接塞进输入框——设置 Text 会触发 TxtInput_TextChanged，预览卡片跟着立刻更新，
        // 不用额外调用。故意不清空"正在编辑：xxx"的提示：多点几下随机只是在改输入框里的草稿内容，
        // 不代表用户放弃了正在编辑的那个已存主题，真正决定"这次保存是新建还是覆盖"的是 _editingFileName，
        // 这里不去动它——点了保存的话照样会覆盖那份正在编辑的主题，这个提示应该继续挂着提醒用户
        private void BtnRandomize_Click(object sender, RoutedEventArgs e)
        {
            HideMessages();
            TxtInput.Text = CustomThemeRandomizer.GenerateJson(CustomThemeAchievement.IsUnlocked());
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

            TxtInput.Text = text; // 触发 TxtInput_TextChanged -> UpdatePreview()，读出来是不是合法主题立刻看得出来

            // 清掉"正在编辑：xxx"的提示——导入的很可能是完全不相关的另一份主题，留着这个提示的话，
            // 用户容易没注意到还在editing一份旧主题，点保存的时候把手头正在编辑的那份意外覆盖掉
            _editingFileName = null;
            TxtEditingHint.Visibility = Visibility.Collapsed;
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
                TxtSuccess.Text = $"✅ 「{entry.Theme.Name}」已导出，把这个文件发给别人，对方在这个页面点 [📥 从文件导入] 就能用。";
                SuccessBox.Visibility = Visibility.Visible;
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

            // 存之前先问一次"主题工匠"解不解锁——这是第一次存主题之前唯一能拿到的"之前"状态，
            // 存完再问一次就已经变成 true 了，没法靠"存后的状态"反推"是不是刚刚才解锁的"
            bool themeMakerUnlockedBefore = CustomThemeAchievement.IsUnlocked();

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

            TxtSuccess.Text = !themeMakerUnlockedBefore && CustomThemeAchievement.IsUnlocked()
                ? $"✅ 「{theme.Name}」保存成功，回到设置页的皮肤选择器里就能看到了。🎉 顺带解锁「主题工匠」成就，🎲 随机生成里多了一份限定配色。"
                : $"✅ 「{theme.Name}」保存成功，回到设置页的皮肤选择器里就能看到了。";
            SuccessBox.Visibility = Visibility.Visible;

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

        private void RefreshThemeList()
        {
            ThemeListPanel.Children.Clear();
            var themes = CustomThemeStore.ListAll();

            TxtThemeCount.Text = $"已保存的客制化主题 ({themes.Count}/{CustomThemeStore.MaxThemes})";
            TxtNoThemes.Visibility = themes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            foreach (var entry in themes)
            {
                var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
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
                    TxtInput.Text = raw;
                    _editingFileName = entry.FileName;
                    TxtEditingHint.Text = $"正在编辑：{entry.Theme.Name}（保存会覆盖更新这一份，不会新建）";
                    TxtEditingHint.Visibility = Visibility.Visible;
                };
                Grid.SetColumn(editBtn, 1);

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
                Grid.SetColumn(exportBtn, 2);

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
                Grid.SetColumn(deleteBtn, 3);

                row.Children.Add(name);
                row.Children.Add(editBtn);
                row.Children.Add(exportBtn);
                row.Children.Add(deleteBtn);
                ThemeListPanel.Children.Add(row);
            }
        }
    }
}
