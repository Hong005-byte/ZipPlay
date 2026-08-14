using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Foundation;
using Windows.Media.Control;
using Forms = System.Windows.Forms;

namespace PixelLyric8BitFix
{
    // 皮肤：把 18 套皮肤（17 套内置 + 客制化）套到界面上——背景/装饰层的显隐、字体/文字调色板、
    // 客制化主题的现场拼装、MC 皮肤 Steve 走路循环，全在这个文件。
    public partial class MainWindow : Window
    {
        // 内置皮肤走 SkinTheme.For；客制化皮肤把用户那份 JSON 现场"翻译"成同一个 SkinTheme 形状，
        // 这样调色板相关的代码（ApplySkinPalette、Mini 小方块）完全不用关心当前是内置还是客制化的。
        //
        // 客制化加载失败（_customTheme 为 null）时要落到 Simple——不能直接丢给 SkinTheme.For(PlayerSkin.Custom)，
        // 那个 switch 没有 Custom 这个分支，会落进默认分支变成 Minecraft 配色，跟 ApplySkin 那边"背景退回简约风"
        // 对不上，会出现"简约风的纯色背景 + Minecraft 的黄绿字"这种背景和文字配色对不上的诡异结果。
        private SkinTheme GetActiveSkinTheme(PlayerSkin skin)
        {
            if (skin == PlayerSkin.Custom)
            {
                return _customTheme != null ? BuildSkinThemeFromCustom(_customTheme) : SkinTheme.For(PlayerSkin.Simple);
            }
            return SkinTheme.For(skin);
        }

        private static SkinTheme BuildSkinThemeFromCustom(CustomTheme custom)
        {
            var colors = custom.Colors!;
            CustomThemeValidator.TryParseHexColor(colors.Title!, out var title);
            CustomThemeValidator.TryParseHexColor(colors.Artist!, out var artist);
            CustomThemeValidator.TryParseHexColor(colors.Accent!, out var accent);
            CustomThemeValidator.TryParseHexColor(colors.Lyric!, out var lyric);
            CustomThemeValidator.TryParseHexColor(string.IsNullOrWhiteSpace(colors.Glow) ? colors.Accent! : colors.Glow, out var glow);
            CustomThemeValidator.TryParseHexColor(colors.LyricBoxBg!, out var lyricBoxBg);
            CustomThemeValidator.TryParseHexColor(colors.LyricBoxBorder!, out var lyricBoxBorder);

            var bgStops = custom.Background!.Stops!
                .Select(s => { CustomThemeValidator.TryParseHexColor(s, out var c); return c; })
                .ToList();
            Color miniBg = bgStops.Count > 0 ? bgStops[0] : Color.FromRgb(0x22, 0x22, 0x22);

            var iconRows = custom.Icon!.Rows!.ToArray();
            var iconPalette = custom.Icon.Palette!.ToDictionary(
                kv => kv.Key[0],
                kv => { CustomThemeValidator.TryParseHexColor(kv.Value, out var c); return c; });
            if (!iconPalette.ContainsKey('.')) iconPalette['.'] = Colors.Transparent;

            return new SkinTheme
            {
                Font = new FontFamily(string.IsNullOrWhiteSpace(custom.Font) ? "Segoe UI" : custom.Font),
                Title = title, Artist = artist, Accent = accent, Lyric = lyric, Glow = glow, GlowBlur = colors.GlowBlur,
                LyricBoxBg = lyricBoxBg, LyricBoxBorder = lyricBoxBorder,
                MiniBg = miniBg, MiniBorder = accent,
                MiniIcon = () => PixelArt.BuildCustomIcon(iconRows, iconPalette),
            };
        }

        // 皮肤：先把 6 套背景层 / 装饰层的显隐都摆对，再套上文字调色板
        private void ApplySkin(PlayerSkin skin)
        {
            // 客制化皮肤要先把 JSON 读出来——加载失败（文件被删/改坏了）就当没有，下面会退回简约风
            _customTheme = skin == PlayerSkin.Custom && !string.IsNullOrEmpty(_settings.CustomThemeFile)
                ? CustomThemeStore.Load(_settings.CustomThemeFile)
                : null;

            MinecraftSkinBg.Visibility = Visibility.Collapsed;
            SimpleSkinBg.Visibility = Visibility.Collapsed;
            CrtSkinBg.Visibility = Visibility.Collapsed;
            CyberpunkSkinBg.Visibility = Visibility.Collapsed;
            VinylSkinBg.Visibility = Visibility.Collapsed;
            GlassSkinBg.Visibility = Visibility.Collapsed;
            LofiSkinBg.Visibility = Visibility.Collapsed;
            AuroraSkinBg.Visibility = Visibility.Collapsed;
            RainSkinBg.Visibility = Visibility.Collapsed;
            StarrySkinBg.Visibility = Visibility.Collapsed;
            CampfireSkinBg.Visibility = Visibility.Collapsed;
            SakuraSkinBg.Visibility = Visibility.Collapsed;
            CassetteSkinBg.Visibility = Visibility.Collapsed;
            CloudSkinBg.Visibility = Visibility.Collapsed;
            CandleSkinBg.Visibility = Visibility.Collapsed;
            PlantSkinBg.Visibility = Visibility.Collapsed;
            SunsetSkinBg.Visibility = Visibility.Collapsed;
            CustomSkinBg.Visibility = Visibility.Collapsed;
            MinecraftDecorCanvas.Visibility = Visibility.Collapsed;
            VinylDecorCanvas.Visibility = Visibility.Collapsed;
            LofiDecorCanvas.Visibility = Visibility.Collapsed;
            CampfireDecorCanvas.Visibility = Visibility.Collapsed;
            SakuraDecorCanvas.Visibility = Visibility.Collapsed;
            CassetteDecorCanvas.Visibility = Visibility.Collapsed;
            CandleDecorCanvas.Visibility = Visibility.Collapsed;
            PlantDecorCanvas.Visibility = Visibility.Collapsed;
            CloudDecorCanvas.Visibility = Visibility.Collapsed;
            SunsetDecorCanvas.Visibility = Visibility.Collapsed;
            CustomIconDecorCanvas.Visibility = Visibility.Collapsed;
            AuroraSnowOverlay.Visibility = Visibility.Collapsed;
            RainOverlay.Visibility = Visibility.Collapsed;
            StarryOverlay.Visibility = Visibility.Collapsed;
            SakuraOverlay.Visibility = Visibility.Collapsed;
            ScanlineOverlay.Visibility = Visibility.Collapsed;
            CloudOverlay.Visibility = Visibility.Collapsed;
            SunsetOverlay.Visibility = Visibility.Collapsed;
            CustomDriftOverlay.Visibility = Visibility.Collapsed;
            CustomFallOverlay.Visibility = Visibility.Collapsed;

            // 只有需要顶部那条装饰行的皮肤才留出这一行，其余皮肤更简洁，不留空行；
            // 云朵/海边黄昏的装饰是铺满整个卡片的天空/海面，不是角落小图标，所以不用占这一行；
            // 客制化皮肤留不留这一行取决于用户选的 animation.type 是不是 drift，在 ApplyCustomSkinVisuals 里单独处理
            RowDecor.Height = (skin == PlayerSkin.Minecraft || skin == PlayerSkin.Vinyl ||
                                skin == PlayerSkin.Lofi || skin == PlayerSkin.Campfire ||
                                skin == PlayerSkin.Cassette || skin == PlayerSkin.Candle ||
                                skin == PlayerSkin.Plant || skin == PlayerSkin.Sakura ||
                                skin == PlayerSkin.Cloud || skin == PlayerSkin.Sunset)
                ? new GridLength(50) : new GridLength(0);

            switch (skin)
            {
                case PlayerSkin.Minecraft:
                    MinecraftSkinBg.Visibility = Visibility.Visible;
                    MinecraftDecorCanvas.Visibility = Visibility.Visible;
                    ImgTree.Source = PixelArt.CreateTree();
                    DirtBrush.ImageSource = PixelArt.CreateDirtTile();
                    _steveFrame1 = PixelArt.CreateSteveFrame1();
                    _steveFrame2 = PixelArt.CreateSteveFrame2();
                    ImgSteve.Source = _steveFrame1;
                    break;

                case PlayerSkin.Crt:
                    CrtSkinBg.Visibility = Visibility.Visible;
                    ScanlineOverlay.Visibility = Visibility.Visible;
                    ScanlineBrush.ImageSource = PixelArt.CreateScanlineTile();
                    break;

                case PlayerSkin.Cyberpunk:
                    CyberpunkSkinBg.Visibility = Visibility.Visible;
                    break;

                case PlayerSkin.Vinyl:
                    VinylSkinBg.Visibility = Visibility.Visible;
                    VinylDecorCanvas.Visibility = Visibility.Visible;
                    break;

                case PlayerSkin.Glass:
                    GlassSkinBg.Visibility = Visibility.Visible;
                    break;

                case PlayerSkin.Lofi:
                    LofiSkinBg.Visibility = Visibility.Visible;
                    LofiDecorCanvas.Visibility = Visibility.Visible;
                    ImgCoffeeCup.Source = PixelArt.CreateCoffeeCup();
                    break;

                case PlayerSkin.Aurora:
                    AuroraSkinBg.Visibility = Visibility.Visible;
                    AuroraSnowOverlay.Visibility = Visibility.Visible;
                    break;

                case PlayerSkin.Rain:
                    RainSkinBg.Visibility = Visibility.Visible;
                    RainOverlay.Visibility = Visibility.Visible;
                    break;

                case PlayerSkin.Starry:
                    StarrySkinBg.Visibility = Visibility.Visible;
                    StarryOverlay.Visibility = Visibility.Visible;
                    break;

                case PlayerSkin.Campfire:
                    CampfireSkinBg.Visibility = Visibility.Visible;
                    CampfireDecorCanvas.Visibility = Visibility.Visible;
                    ImgCampfire.Source = PixelArt.CreateCampfire();
                    break;

                case PlayerSkin.Sakura:
                    SakuraSkinBg.Visibility = Visibility.Visible;
                    SakuraOverlay.Visibility = Visibility.Visible;
                    SakuraDecorCanvas.Visibility = Visibility.Visible;
                    ImgSakuraTree.Source = PixelArt.CreateSakuraTree();
                    break;

                case PlayerSkin.Cassette:
                    CassetteSkinBg.Visibility = Visibility.Visible;
                    CassetteDecorCanvas.Visibility = Visibility.Visible;
                    break;

                case PlayerSkin.Cloud:
                    CloudSkinBg.Visibility = Visibility.Visible;
                    CloudOverlay.Visibility = Visibility.Visible;
                    CloudDecorCanvas.Visibility = Visibility.Visible;
                    ImgBalloon.Source = PixelArt.CreateHotAirBalloon();
                    StartBobAnimation(BalloonBobTransform, 3.2, 4);
                    break;

                case PlayerSkin.Candle:
                    CandleSkinBg.Visibility = Visibility.Visible;
                    CandleDecorCanvas.Visibility = Visibility.Visible;
                    ImgCandle.Source = PixelArt.CreateCandle();
                    break;

                case PlayerSkin.Plant:
                    PlantSkinBg.Visibility = Visibility.Visible;
                    PlantDecorCanvas.Visibility = Visibility.Visible;
                    ImgPlant.Source = PixelArt.CreatePlant();
                    break;

                case PlayerSkin.Sunset:
                    SunsetSkinBg.Visibility = Visibility.Visible;
                    SunsetOverlay.Visibility = Visibility.Visible;
                    SunsetDecorCanvas.Visibility = Visibility.Visible;
                    ImgSailboat.Source = PixelArt.CreateSailboat();
                    StartBobAnimation(SailboatBobTransform, 2.6, 3);
                    break;

                case PlayerSkin.Custom:
                    if (_customTheme != null)
                    {
                        ApplyCustomSkinVisuals(_customTheme);
                    }
                    else
                    {
                        // 客制化主题加载失败（文件被删/改坏了）：退回简约风，不能让整个窗口崩掉
                        SimpleSkinBg.Visibility = Visibility.Visible;
                    }
                    break;

                case PlayerSkin.Simple:
                default:
                    SimpleSkinBg.Visibility = Visibility.Visible;
                    break;
            }

            ApplySkinPalette(skin);
        }

        // 客制化皮肤的视觉层：背景（纯色/渐变）+ 用户的像素图标 + 挑一种内置动画"招式"。
        // 跟前面 17 套不一样，这一套完全是数据驱动现场拼出来的，不是写死的 XAML 块。
        private void ApplyCustomSkinVisuals(CustomTheme theme)
        {
            var stops = theme.Background!.Stops!
                .Select(s => { CustomThemeValidator.TryParseHexColor(s, out var c); return c; })
                .ToList();

            Brush bgBrush;
            if (string.Equals(theme.Background.Type, "gradient", StringComparison.OrdinalIgnoreCase) && stops.Count >= 2)
            {
                var gradient = new LinearGradientBrush
                {
                    StartPoint = new System.Windows.Point(0, 0),
                    EndPoint = string.Equals(theme.Background.Direction, "diagonal", StringComparison.OrdinalIgnoreCase)
                        ? new System.Windows.Point(1, 1) : new System.Windows.Point(0, 1),
                };
                for (int i = 0; i < stops.Count; i++)
                {
                    gradient.GradientStops.Add(new GradientStop(stops[i], stops.Count == 1 ? 0 : (double)i / (stops.Count - 1)));
                }
                bgBrush = gradient;
            }
            else
            {
                bgBrush = new SolidColorBrush(stops.Count > 0 ? stops[0] : Colors.Black);
            }

            CustomThemeValidator.TryParseHexColor(theme.Colors!.Accent!, out var accent);

            CustomSkinBg.Background = bgBrush;
            CustomSkinBg.BorderBrush = new SolidColorBrush(accent);
            CustomSkinGlow.Color = accent;
            CustomSkinBg.Visibility = Visibility.Visible;

            var iconPalette = theme.Icon!.Palette!.ToDictionary(
                kv => kv.Key[0],
                kv => { CustomThemeValidator.TryParseHexColor(kv.Value, out var c); return c; });
            if (!iconPalette.ContainsKey('.')) iconPalette['.'] = Colors.Transparent;
            var iconBitmap = PixelArt.BuildCustomIcon(theme.Icon.Rows!.ToArray(), iconPalette);

            string animType = theme.Animation!.Type!.ToLowerInvariant();
            double? customDuration = theme.Animation.Duration;

            if (animType == "drift")
            {
                RowDecor.Height = new GridLength(0);
                CustomDriftOverlay.Visibility = Visibility.Visible;
                CustomDriftIcon1.Source = iconBitmap;
                CustomDriftIcon2.Source = iconBitmap;
                CustomDriftIcon3.Source = iconBitmap;
                StartCustomDriftAnimation(CustomDrift1Transform, customDuration ?? 14, 0);
                StartCustomDriftAnimation(CustomDrift2Transform, (customDuration ?? 14) * 1.35, 2);
                StartCustomDriftAnimation(CustomDrift3Transform, (customDuration ?? 14) * 1.7, 5);
            }
            else if (animType == "fall")
            {
                RowDecor.Height = new GridLength(0);
                CustomFallOverlay.Visibility = Visibility.Visible;
                CustomFallIcon1.Source = iconBitmap;
                CustomFallIcon2.Source = iconBitmap;
                CustomFallIcon3.Source = iconBitmap;
                StartCustomFallAnimation(CustomFall1Transform, customDuration ?? 6, 0, -6, 8);
                StartCustomFallAnimation(CustomFall2Transform, (customDuration ?? 6) * 1.3, 1.5, 4, -10);
                StartCustomFallAnimation(CustomFall3Transform, (customDuration ?? 6) * 1.6, 3, -8, 6);
            }
            else
            {
                RowDecor.Height = new GridLength(50);
                CustomIconDecorCanvas.Visibility = Visibility.Visible;
                CustomIcon.Source = iconBitmap;
                StartCustomIconAnimation(animType, customDuration, accent);
            }
        }

        // "飘过型"：同一个图标横向飘过整张卡片，飘到头瞬间重置回最左边——
        // 跟 Cloud/Rain/雪花那几个内置皮肤用的是同一套手法
        private void StartCustomDriftAnimation(TranslateTransform transform, double durationSeconds, double beginDelaySeconds)
        {
            var anim = new DoubleAnimation
            {
                From = -30,
                To = 330,
                Duration = TimeSpan.FromSeconds(SafeDuration(durationSeconds, 14)),
                BeginTime = TimeSpan.FromSeconds(beginDelaySeconds),
                RepeatBehavior = RepeatBehavior.Forever,
            };
            transform.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        // "飘落型"：同一个图标从卡片顶部飘到底部，飘到头瞬间重置回最上面，叠加一点左右轻摆（比纯直线
        // 下落更自然）——跟内置的樱花/极光雪花那几个皮肤是同一套手法。摇摆的时长故意比下落短很多
        // （下落时长的 1/3.5），来回摆好几下才落地一次，摆动感才看得出来，不会显得像在匀速平移。
        private void StartCustomFallAnimation(TranslateTransform transform, double durationSeconds, double beginDelaySeconds, double swayFrom, double swayTo)
        {
            double fallSeconds = SafeDuration(durationSeconds, 6);

            var fallAnim = new DoubleAnimation
            {
                From = -20,
                To = 170,
                Duration = TimeSpan.FromSeconds(fallSeconds),
                BeginTime = TimeSpan.FromSeconds(beginDelaySeconds),
                RepeatBehavior = RepeatBehavior.Forever,
            };
            transform.BeginAnimation(TranslateTransform.YProperty, fallAnim);

            var swayAnim = new DoubleAnimation
            {
                From = swayFrom,
                To = swayTo,
                Duration = TimeSpan.FromSeconds(fallSeconds / 3.5),
                BeginTime = TimeSpan.FromSeconds(beginDelaySeconds),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
            };
            transform.BeginAnimation(TranslateTransform.XProperty, swayAnim);
        }

        // "浮动型"（bob）：纯粹的位置上下浮动，缓入缓出——跟 sway（绕轴心转角度摆动）是两个不同的动作。
        // 海边黄昏的帆船停在海面上、云朵漂浮的热气球飘在天上，都用这同一个方法，只是幅度/时长不同；
        // 这个动作也加进了客制化主题的第 8 种可选招式，见 StartCustomIconAnimation 里的 "bob" 分支。
        private void StartBobAnimation(TranslateTransform transform, double durationSeconds, double amplitude)
        {
            var anim = new DoubleAnimation(-amplitude, amplitude, TimeSpan.FromSeconds(SafeDuration(durationSeconds, 3)))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            transform.BeginAnimation(TranslateTransform.YProperty, anim);
        }

        // CustomThemeValidator 已经要求 duration 必须 > 0，但这里再兜底一层：万一是加校验之前存的老文件
        // （或者以后校验逻辑本身有疏漏），也不会因为 0/负数时长让 WPF 的动画系统直接抛异常崩掉整个 App。
        private static double SafeDuration(double? value, double fallback) =>
            value.HasValue && value.Value > 0 ? value.Value : fallback;

        // 单图标动画："招式"从 pulse / twinkle / sway / spin / flicker 里选一个，全部用代码现场构造
        // DoubleAnimation，不需要预先在 XAML 里声明 Storyboard 资源
        private void StartCustomIconAnimation(string type, double? customDuration, Color accent)
        {
            CustomIconRotate.Angle = 0;
            CustomIcon.Opacity = 1;
            CustomIconGlow.Color = accent;
            CustomIconGlow.Opacity = 0.6;

            switch (type)
            {
                case "pulse": // 呼吸发光，平缓的明暗循环
                    {
                        var anim = new DoubleAnimation(0.35, 0.75, TimeSpan.FromSeconds(SafeDuration(customDuration, 2.2)))
                        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                        CustomIconGlow.BeginAnimation(DropShadowEffect.OpacityProperty, anim);
                        break;
                    }
                case "twinkle": // 图标本体渐隐渐现，比 pulse 幅度更大、更像星星闪烁
                    {
                        var anim = new DoubleAnimation(0.25, 1.0, TimeSpan.FromSeconds(SafeDuration(customDuration, 1.6)))
                        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                        CustomIcon.BeginAnimation(UIElement.OpacityProperty, anim);
                        break;
                    }
                case "sway": // 以图标底部为轴心左右轻摆，跟绿植角落皮肤同一套手法
                    {
                        var anim = new DoubleAnimation(-8, 8, TimeSpan.FromSeconds(SafeDuration(customDuration, 3.2)))
                        {
                            AutoReverse = true,
                            RepeatBehavior = RepeatBehavior.Forever,
                            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                        };
                        CustomIconRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
                        break;
                    }
                case "spin": // 匀速转圈，跟黑胶/磁带卷盘同一套手法
                    {
                        var anim = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(SafeDuration(customDuration, 4)))
                        { RepeatBehavior = RepeatBehavior.Forever };
                        CustomIconRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
                        break;
                    }
                case "flicker": // 不规则明暗跳动，跟篝火/赛博朋克同一套手法，节奏比 pulse/twinkle 更"毛躁"
                    {
                        double dur = SafeDuration(customDuration, 2.0);
                        var frames = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
                        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.5, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.75, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(dur * 0.15))));
                        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.4, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(dur * 0.3))));
                        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.7, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(dur * 0.42))));
                        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.5, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(dur))));
                        CustomIconGlow.BeginAnimation(DropShadowEffect.OpacityProperty, frames);
                        break;
                    }
                case "bob": // 上下轻轻浮动，跟海边黄昏的帆船、云朵漂浮的热气球同一套手法——纯位置浮动，
                            // 不是旋转摆动（那是 sway），振幅比内置那两个稍大一点，客制化图标通常比装饰物更显眼一些
                    StartBobAnimation(CustomIconBob, customDuration ?? 3, 5);
                    break;
            }
        }

        // 每套皮肤自己的字体 / 文字颜色 / 歌词框配色 / 发光效果——具体数据全在 SkinTheme 里，这里只管往控件上贴
        private void ApplySkinPalette(PlayerSkin skin)
        {
            var t = GetActiveSkinTheme(skin);

            TxtSongTitle.FontFamily = t.Font;
            TxtArtist.FontFamily = t.Font;
            TxtTime.FontFamily = t.Font;
            TxtDynamicLyric.FontFamily = t.Font;
            TxtTranslationLyric.FontFamily = t.Font;

            TxtSongTitle.Foreground = new SolidColorBrush(t.Title);
            TxtArtist.Foreground = new SolidColorBrush(t.Artist);
            TxtTime.Foreground = new SolidColorBrush(t.Artist);

            LyricProgressBar.Foreground = new SolidColorBrush(t.Accent);
            LyricProgressBar.Effect = new DropShadowEffect { Color = t.Accent, BlurRadius = 5, ShadowDepth = 0 };

            LyricBox.Background = new SolidColorBrush(t.LyricBoxBg);
            LyricBox.BorderBrush = new SolidColorBrush(t.LyricBoxBorder);

            TxtDynamicLyric.Foreground = new SolidColorBrush(t.Lyric);
            TxtDynamicLyric.Effect = t.GlowBlur > 0
                ? new DropShadowEffect { Color = t.Glow, BlurRadius = t.GlowBlur, ShadowDepth = 0, Opacity = 0.9 }
                : null;

            // 歌词字号：设置页里选的小/中/大，主歌词行用原始磅值，翻译行故意小一号（0.72x），
            // 视觉上一眼能分清"这是原文、那是翻译"，不用额外加颜色以外的区分手段
            TxtDynamicLyric.FontSize = _settings.GetLyricFontSizePt();
            TxtTranslationLyric.FontSize = Math.Max(9, _settings.GetLyricFontSizePt() * 0.72);

            // 双语歌词的翻译行颜色：跟主歌词同色但降透明度，比卡拉OK"未唱"部分（0x66）稍微实一点，
            // 毕竟这整行平时都是这个颜色，太淡会看不清；不用给 SkinTheme 单独加字段，任何皮肤配出来都自动协调
            TxtTranslationLyric.Foreground = new SolidColorBrush(Color.FromArgb(0xCC, t.Lyric.R, t.Lyric.G, t.Lyric.B));

            // 卡拉OK扫光的两种颜色：已经唱到的部分用皮肤原本的歌词亮色（跟以前整行显示时一样），
            // 还没唱到的部分用同一个颜色降透明度，不用给 SkinTheme 单独加字段，任何皮肤配出来都自动协调
            _karaokeSungBrush = new SolidColorBrush(t.Lyric);
            _karaokeUnsungBrush = new SolidColorBrush(Color.FromArgb(0x66, t.Lyric.R, t.Lyric.G, t.Lyric.B));
            _lastKaraokeLineIndex = -1; // 换皮肤了，强制下一 tick 重新渲染一遍当前这句
            _lastKaraokeSungChars = -1;

            UpdateKaraokeToggleIcon();

            // 播放控制那三个图标（上一首/播放-暂停/下一首）用皮肤的强调色，跟进度条同一个颜色，
            // 视觉上是一组的；按钮底色是固定的半透明黑，不跟着变，保证图标颜色深浅都看得清
            var playbackIconBrush = new SolidColorBrush(t.Accent);
            PrevIconBar.Fill = playbackIconBrush;
            PrevIconTriangle.Fill = playbackIconBrush;
            PlayIcon.Fill = playbackIconBrush;
            PauseIconBar1.Fill = playbackIconBrush;
            PauseIconBar2.Fill = playbackIconBrush;
            NextIconTriangle.Fill = playbackIconBrush;
            NextIconBar.Fill = playbackIconBrush;

            // 进度条上那个可拖拽的图标，用的是跟 Mini 小方块同一张皮肤图标——同一个视觉符号
            // 在两个地方（Mini 小方块、进度条拖拽点）都出现，"这是当前这套皮肤的标志物"这件事更一致
            SeekThumbIcon.Source = t.MiniIcon();
        }

        // Steve 来回走：范围按窗口实际宽度算，不再是写死的 20~420（小尺寸会走出界，大尺寸又用不满）
        private void StartSteveWalking()
        {
            double rightBound = Math.Max(60, Width - 110); // 110 = 两侧 Canvas 内边距 + 预留给树的位置
            var duration = TimeSpan.FromSeconds(7);

            var xAnim = new DoubleAnimation(20, rightBound, duration) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            SteveTransform.BeginAnimation(TranslateTransform.XProperty, xAnim);

            var shadowAnim = new DoubleAnimation(20, rightBound, duration) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            ShadowTransform.BeginAnimation(TranslateTransform.XProperty, shadowAnim);

            _steveLastX = 20;
        }

        // 每 tick 检查一次：走路换腿（约 250ms 一帧）+ 按位移方向翻面朝向
        private void UpdateSteveWalkAnimation()
        {
            double currentX = SteveTransform.X;

            if (currentX > _steveLastX + 0.05) SteveFlip.ScaleX = 1;   // 在往右走，面朝右
            else if (currentX < _steveLastX - 0.05) SteveFlip.ScaleX = -1; // 在往左走，翻转面朝左
            _steveLastX = currentX;

            _steveLegTickCounter++;
            if (_steveLegTickCounter >= 5) // 5 × 50ms ≈ 250ms 切一次腿部姿势
            {
                _steveLegTickCounter = 0;
                ImgSteve.Source = ImgSteve.Source == _steveFrame1 ? _steveFrame2 : _steveFrame1;
            }
        }

        // 显示模式：极简只留歌词一行，标准把标题/进度条也带上
        private void ApplyDisplayMode(PlayerDisplayMode mode)
        {
            bool minimal = mode == PlayerDisplayMode.Minimal;

            TitleRow.Visibility = minimal ? Visibility.Collapsed : Visibility.Visible;
            ProgressRow.Visibility = minimal ? Visibility.Collapsed : Visibility.Visible;
            RowTitle.Height = minimal ? new GridLength(0) : GridLength.Auto;
            RowProgress.Height = minimal ? new GridLength(0) : GridLength.Auto;

            // 极简模式就是"什么都不要，只看歌词"，播放控制这一列也跟着收起来
            PlaybackControlsPanel.Visibility = minimal ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
