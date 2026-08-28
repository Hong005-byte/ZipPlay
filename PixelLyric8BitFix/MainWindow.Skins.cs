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

        // internal（不是 private）：ListeningStatsWindow 生成分享卡片时，"卡片风格"选了某个客制化主题
        // 也要能拿到同一份 SkinTheme（配色 + 图标），不用为了那一个场景再重新实现一遍这个转换逻辑
        internal static SkinTheme BuildSkinThemeFromCustom(CustomTheme custom)
        {
            var colors = custom.Colors!;
            CustomThemeColorInterop.TryParseHexColor(colors.Title!, out var title);
            CustomThemeColorInterop.TryParseHexColor(colors.Artist!, out var artist);
            CustomThemeColorInterop.TryParseHexColor(colors.Accent!, out var accent);
            CustomThemeColorInterop.TryParseHexColor(colors.Lyric!, out var lyric);
            CustomThemeColorInterop.TryParseHexColor(string.IsNullOrWhiteSpace(colors.Glow) ? colors.Accent! : colors.Glow, out var glow);
            CustomThemeColorInterop.TryParseHexColor(colors.LyricBoxBg!, out var lyricBoxBg);
            CustomThemeColorInterop.TryParseHexColor(colors.LyricBoxBorder!, out var lyricBoxBorder);

            var bgStops = custom.Background!.Stops!
                .Select(s => { CustomThemeColorInterop.TryParseHexColor(s, out var c); return c; })
                .ToList();
            Color miniBg = bgStops.Count > 0 ? bgStops[0] : Color.FromRgb(0x22, 0x22, 0x22);

            // 有 icon.frames 的话 Rows 就是 null（渲染只认 frames，见 CustomThemeIcon.Frames 的注释）——
            // MiniIcon 这条路（Mini 小方块/进度条拖拽图标/分享卡片吉祥物）目前还没接上逐帧动画，见
            // ApplyCustomSkinVisuals 开头的范围说明，先退到第一帧当静态图，不是漏了处理 frames，
            // 是刻意先只让主图标动。之前这里无脑读 Rows!，遇到只写了 frames、没写 rows 的主题会直接
            // NullReferenceException——凡是选了带 frames 的自定义主题当皮肤，一进播放器就崩。
            var iconRows = (custom.Icon!.Frames is { Count: > 0 } frames ? frames[0] : custom.Icon.Rows!).ToArray();
            var iconPalette = custom.Icon.Palette!.ToDictionary(
                kv => kv.Key[0],
                kv => { CustomThemeColorInterop.TryParseHexColor(kv.Value, out var c); return c; });
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
        // 顶部装饰图标的整体缩放——"窗口与显示"页那个滑块调的对象。缩放锚点定在每个装饰画布自己的
        // 右下角（RenderTransformOrigin="1,1"）：这些画布本来就撑满整条顶部装饰行（RowDecor，紧贴着
        // 下面的歌词框），而各套皮肤的图标基本都贴着画布的右下角摆（Canvas.Right/Canvas.Bottom）。
        // 从右下角往外缩放，放大的部分只会往左上（窗口内部、离歌词框更远的方向）长，不会反过来长到
        // 歌词框那一侧；缩得比较大的话顶多是图标顶部超出窗口本身的可见范围被窗口边缘裁掉，不会盖到
        // 别的内容。21 套皮肤各自的装饰画布统一走这一份，不用现查"现在是哪个皮肤"再单独处理——
        // 没显示的画布带不带这个变换都不影响任何东西。
        private void ApplyDecorIconScale(double scale)
        {
            Canvas[] canvases =
            {
                MinecraftDecorCanvas, VinylDecorCanvas, LofiDecorCanvas, CampfireDecorCanvas, SakuraDecorCanvas,
                CassetteDecorCanvas, CandleDecorCanvas, PlantDecorCanvas, CloudDecorCanvas, SunsetDecorCanvas,
                StarryDecorCanvas, CrtDecorCanvas, GlassDecorCanvas, AuroraDecorCanvas, RainDecorCanvas,
                CyberpunkDecorCanvas, ArcadeDecorCanvas, InvadersDecorCanvas, CityDecorCanvas, CrownDecorCanvas,
                CustomIconDecorCanvas,
            };
            foreach (var canvas in canvases)
            {
                canvas.RenderTransformOrigin = new System.Windows.Point(1, 1); // 这个文件同时 using 了 Windows.Foundation（SMTC），跟 System.Windows 都有个 Point，得写全名消歧义
                canvas.RenderTransform = new ScaleTransform(scale, scale);
            }
        }

        private void ApplySkin(PlayerSkin skin)
        {
            // 目前 ApplySkin 只会在构造函数里跑一次（换皮肤是整个 MainWindow 重开，不是同一个实例复用），
            // 所以这里清空严格来说还用不上——但音乐律动那批 Storyboard 是 Add 进 _musicReactiveStoryboards
            // 的，没有对应的"清空"动作，纯粹是这个 List 本身没有"只允许当前皮肤这几个"的约束。先在这里
            // 清一下，万一以后哪天改成"不重开窗口、原地切皮肤"，也不会因为忘了清空导致旧皮肤的 Storyboard
            // 一直占在列表里、UpdateMusicReactiveSkin 每 tick 都在给不可见的动画调速率
            _musicReactiveStoryboards.Clear();

            // 逐帧动画状态也要清一次——不清的话，从"带 frames 的客制化主题"切到别的皮肤（或者切到
            // 另一份没有 frames 的客制化主题）之后，SmoothTimer_Tick 里 `if (_customIconFrames != null)`
            // 那个判断会一直命中，拿着上一份主题的位图数组去更新一个现在压根不显示这份图标的皮肤，
            // 纯属浪费；ApplyCustomSkinVisuals 命中带 frames 的主题时会重新赋值回来，不会漏
            _customIconFrames = null;

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
            ArcadeSkinBg.Visibility = Visibility.Collapsed;
            InvadersSkinBg.Visibility = Visibility.Collapsed;
            CitySkinBg.Visibility = Visibility.Collapsed;
            CrownSkinBg.Visibility = Visibility.Collapsed;
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
            StarryDecorCanvas.Visibility = Visibility.Collapsed;
            CrtDecorCanvas.Visibility = Visibility.Collapsed;
            GlassDecorCanvas.Visibility = Visibility.Collapsed;
            AuroraDecorCanvas.Visibility = Visibility.Collapsed;
            RainDecorCanvas.Visibility = Visibility.Collapsed;
            CyberpunkDecorCanvas.Visibility = Visibility.Collapsed;
            ArcadeDecorCanvas.Visibility = Visibility.Collapsed;
            InvadersDecorCanvas.Visibility = Visibility.Collapsed;
            CityDecorCanvas.Visibility = Visibility.Collapsed;
            CrownDecorCanvas.Visibility = Visibility.Collapsed;
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
                                skin == PlayerSkin.Cloud || skin == PlayerSkin.Sunset ||
                                skin == PlayerSkin.Starry || skin == PlayerSkin.Crt ||
                                skin == PlayerSkin.Glass || skin == PlayerSkin.Aurora ||
                                skin == PlayerSkin.Rain || skin == PlayerSkin.Cyberpunk ||
                                skin == PlayerSkin.Arcade || skin == PlayerSkin.Invaders ||
                                skin == PlayerSkin.City || skin == PlayerSkin.Crown)
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
                    CrtDecorCanvas.Visibility = Visibility.Visible;
                    ImgRetroTv.Source = PixelArt.CreateRetroTv();
                    break;

                case PlayerSkin.Cyberpunk:
                    CyberpunkSkinBg.Visibility = Visibility.Visible;
                    CyberpunkDecorCanvas.Visibility = Visibility.Visible;
                    ImgHoloRobot.Source = PixelArt.CreateHoloRobot();
                    StartBobAnimation(HoloRobotBobTransform, 2.4, 3);
                    break;

                case PlayerSkin.Vinyl:
                    VinylSkinBg.Visibility = Visibility.Visible;
                    VinylDecorCanvas.Visibility = Visibility.Visible;
                    break;

                case PlayerSkin.Glass:
                    GlassSkinBg.Visibility = Visibility.Visible;
                    GlassDecorCanvas.Visibility = Visibility.Visible;
                    ImgCrystal.Source = PixelArt.CreateCrystal();
                    StartBobAnimation(CrystalBobTransform, 2.8, 3);
                    break;

                case PlayerSkin.Lofi:
                    LofiSkinBg.Visibility = Visibility.Visible;
                    LofiDecorCanvas.Visibility = Visibility.Visible;
                    ImgCoffeeCup.Source = PixelArt.CreateCoffeeCup();
                    break;

                case PlayerSkin.Aurora:
                    AuroraSkinBg.Visibility = Visibility.Visible;
                    AuroraSnowOverlay.Visibility = Visibility.Visible;
                    AuroraDecorCanvas.Visibility = Visibility.Visible;
                    ImgArcticFox.Source = PixelArt.CreateArcticFox();
                    StartBobAnimation(ArcticFoxBobTransform, 3.6, 1.5); // 幅度很小，安静蹲着、只有极轻的呼吸感，不抢戏
                    break;

                case PlayerSkin.Rain:
                    RainSkinBg.Visibility = Visibility.Visible;
                    RainOverlay.Visibility = Visibility.Visible;
                    RainDecorCanvas.Visibility = Visibility.Visible;
                    ImgWindowsillCat.Source = PixelArt.CreateWindowsillCat();
                    StartBobAnimation(WindowsillCatBobTransform, 3.6, 1.5); // 同上，趴窗台上的猫也只要极轻微的呼吸感
                    break;

                case PlayerSkin.Starry:
                    StarrySkinBg.Visibility = Visibility.Visible;
                    StarryOverlay.Visibility = Visibility.Visible;
                    StarryDecorCanvas.Visibility = Visibility.Visible;
                    ImgUfo.Source = PixelArt.CreateUfo();
                    StartUfoDrift();
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

                case PlayerSkin.Arcade:
                    ArcadeSkinBg.Visibility = Visibility.Visible;
                    ArcadeDecorCanvas.Visibility = Visibility.Visible;
                    ImgArcadeCabinet.Source = PixelArt.CreateArcadeCabinet();
                    break;

                case PlayerSkin.Invaders:
                    InvadersSkinBg.Visibility = Visibility.Visible;
                    InvadersDecorCanvas.Visibility = Visibility.Visible;
                    ImgInvader1.Source = PixelArt.CreateInvader();
                    ImgInvader2.Source = PixelArt.CreateInvader();
                    StartInvadersMarch();
                    break;

                case PlayerSkin.City:
                    CitySkinBg.Visibility = Visibility.Visible;
                    CityDecorCanvas.Visibility = Visibility.Visible;
                    ImgTrain.Source = PixelArt.CreateTrain();
                    StartTrainDrift();
                    break;

                case PlayerSkin.Crown:
                    CrownSkinBg.Visibility = Visibility.Visible;
                    CrownDecorCanvas.Visibility = Visibility.Visible;
                    ImgCrown.Source = PixelArt.CreateCrown();
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
                .Select(s => { CustomThemeColorInterop.TryParseHexColor(s, out var c); return c; })
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

            CustomThemeColorInterop.TryParseHexColor(theme.Colors!.Accent!, out var accent);

            CustomSkinBg.Background = bgBrush;
            CustomSkinBg.BorderBrush = new SolidColorBrush(accent);
            CustomSkinGlow.Color = accent;
            CustomSkinBg.Visibility = Visibility.Visible;

            // frames 数组：没有 icon.frames 就是长度 1（跟这个字段加进来之前完全一样的行为）。
            // iconBitmap 取第一帧——这个方法末尾的 ApplyCustomExtraLayers（额外装饰层）暂时还只认
            // 第一帧（静态）；drift/fall 这两条轨道现在也支持逐帧切换了（跟主图标固定贴装饰栏那个
            // 分支走同一套 _customIconFrames 状态，只是 _customIconFrameApply 写去不同的 Image）。
            var iconFrames = CustomThemeColorInterop.BuildCustomIconFrames(theme.Icon!);
            var iconBitmap = iconFrames[0];

            // 每次真正应用一个客制化主题都重新走一遍——点击切换过动作的话，换皮肤/重开窗口要从
            // "动作 0"（图标自己的 Rows/Frames）重新开始，不该记着上次切到了第几个
            _customIconActionIndex = 0;
            _customIconFrames = null;
            _customIconFrameApply = null;
            _customIconActiveAnimationOverride = null; // 动作 0 没有自己的 animation，永远落回下面这份顶层的
            bool hasActions = theme.Icon!.Actions is { Count: > 0 };

            // animation.type 可以是 "pulse" 这种单招，也可以是 "pulse+sway" 这种用 + 连起来的组合——
            // 校验已经保证：要么整个数组只有 drift 或 fall 一个（走三图标飘过/飘落轨道），要么完全不含
            // drift/fall（走下面 else 分支，可以是 1~6 招的任意组合）。两种情况不会混在一起，这里不用
            // 再重新判一遍"含不含 drift/fall"，直接看 animTypes[0] 是不是那两个之一就够了。
            string[] animTypes = CustomThemeValidator.SplitAnimationTypes(theme.Animation!.Type!);
            double? customDuration = theme.Animation.Duration;
            // "跟着音乐律动"是通用开关，不挑招式组合里的哪一个——不管选了几种，都统一用 SpeedRatio
            // 让这些动画的播放速度跟着音乐响度/鼓点变，见 BeginMusicReactiveAnimation
            bool musicReactive = theme.Animation.MusicReactive && _settings.SkinAudioReactiveEnabled;
            // "反应多强"跟"要不要反应"是两回事——sensitivity 只在 musicReactive 为 true 时才有意义，
            // 这里提前算好传下去，各个 Start*Animation 不用各自再判一遍 MusicReactive 开关
            double sensitivity = CustomThemeValidator.SensitivityToMultiplier(theme.Animation.Sensitivity);

            // 统一"启用逐帧切换"这一步，三个分支（drift/fall/主图标）共用——只是把新的一帧画去哪个/
            // 哪几个 Image 不一样（drift/fall 是三份重影图标一起换，主图标只有它自己）。立刻应用一次
            // 第一帧，不用等 SmoothTimer_Tick 第一次跑到才有内容；只有真的 >1 帧才建立逐帧状态，
            // 1 帧（或者压根没填 frames，且没有 actions）就跟这些字段加进来之前完全一样，
            // SmoothTimer_Tick 里 `if (_customIconFrames != null)` 天然不会命中，不会多一份空转开销。
            void EnableCustomIconFrames(BitmapSource[] frames, Action<BitmapSource> applyFrame)
            {
                applyFrame(frames[0]);
                _customIconFrameApply = applyFrame;
                if (frames.Length > 1)
                {
                    _customIconFrames = frames;
                    _customIconFrameDurationSeconds = CustomThemeValidator.GetFrameDurationSeconds(theme.Icon!);
                    _customIconFrameIndex = 0;
                    _customIconFrameTickCounter = 0;
                    _customIconFramesMusicReactive = musicReactive;
                    _customIconFrameSensitivity = sensitivity;
                    _customIconFrameSpeedRatio = 1.0;
                }
            }

            if (animTypes[0] == "drift")
            {
                RowDecor.Height = new GridLength(0);
                CustomDriftOverlay.Visibility = Visibility.Visible;
                CustomDriftIcon1.Cursor = CustomDriftIcon2.Cursor = CustomDriftIcon3.Cursor = hasActions ? Cursors.Hand : Cursors.Arrow;
                EnableCustomIconFrames(iconFrames, bmp => CustomDriftIcon1.Source = CustomDriftIcon2.Source = CustomDriftIcon3.Source = bmp);
                StartCustomDriftAnimation(CustomDrift1Transform, customDuration ?? 14, 0, musicReactive, sensitivity);
                StartCustomDriftAnimation(CustomDrift2Transform, (customDuration ?? 14) * 1.35, 2, musicReactive, sensitivity);
                StartCustomDriftAnimation(CustomDrift3Transform, (customDuration ?? 14) * 1.7, 5, musicReactive, sensitivity);
            }
            else if (animTypes[0] == "fall")
            {
                RowDecor.Height = new GridLength(0);
                CustomFallOverlay.Visibility = Visibility.Visible;
                CustomFallIcon1.Cursor = CustomFallIcon2.Cursor = CustomFallIcon3.Cursor = hasActions ? Cursors.Hand : Cursors.Arrow;
                EnableCustomIconFrames(iconFrames, bmp => CustomFallIcon1.Source = CustomFallIcon2.Source = CustomFallIcon3.Source = bmp);
                StartCustomFallAnimation(CustomFall1Transform, customDuration ?? 6, 0, -6, 8, musicReactive, sensitivity);
                StartCustomFallAnimation(CustomFall2Transform, (customDuration ?? 6) * 1.3, 1.5, 4, -10, musicReactive, sensitivity);
                StartCustomFallAnimation(CustomFall3Transform, (customDuration ?? 6) * 1.6, 3, -8, 6, musicReactive, sensitivity);
            }
            else
            {
                RowDecor.Height = new GridLength(50);
                CustomIconDecorCanvas.Visibility = Visibility.Visible;
                CustomIcon.Cursor = hasActions ? Cursors.Hand : Cursors.Arrow;
                EnableCustomIconFrames(iconFrames, bmp => CustomIcon.Source = bmp);
                // 重置放在循环外面，只做一次——挪进循环里的话，组合里后一招重置的时候会把前一招刚设好的
                // 状态（比如 sway 已经在转的角度）擦掉，等于每加一招都在跟前面打架
                ResetCustomIconAnimationState(accent);
                foreach (var t in animTypes) StartCustomIconAnimation(t, customDuration, musicReactive, sensitivity);
            }

            ApplyCustomExtraLayers(theme, accent);
        }

        // 点击装饰图标（CustomIcon_MouseLeftButtonDown）触发：循环切到下一个 icon.actions，绕完一圈
        // 回到"动作 0"（图标自己的 Rows/Frames）。换帧之外，如果这个动作自己带了一份 animation
        // （CustomThemeIconAction.Animation），也在这里一并切过去——见下面 ApplyCustomIconActionAnimation。
        // BuildCustomIconFrames 本来就是纯粹"数据转位图"，借同一份 Palette 换一套 Frames 现造一个
        // CustomThemeIcon 传进去，不用改这个方法一行。
        private void CycleCustomIconAction()
        {
            if (_customTheme?.Icon is not { } icon || _customIconFrameApply is not { } applyFrame) return;

            var actions = icon.Actions ?? new List<CustomThemeIconAction>();
            _customIconActionIndex = (_customIconActionIndex + 1) % (actions.Count + 1);
            CustomThemeIconAction? selectedAction = _customIconActionIndex == 0 ? null : actions[_customIconActionIndex - 1];

            BitmapSource[] frames;
            double frameDurationSeconds;
            if (selectedAction == null)
            {
                frames = CustomThemeColorInterop.BuildCustomIconFrames(icon);
                frameDurationSeconds = CustomThemeValidator.GetFrameDurationSeconds(icon);
            }
            else
            {
                frames = CustomThemeColorInterop.BuildCustomIconFrames(new CustomThemeIcon { Palette = icon.Palette, Frames = selectedAction.Frames });
                frameDurationSeconds = selectedAction.FrameDuration is double d && d > 0 ? d : CustomThemeValidator.GetFrameDurationSeconds(icon);
            }

            _customIconFrames = frames.Length > 1 ? frames : null; // 只有 1 帧就没什么好"切换"的，跟别处的判断一致
            _customIconFrameDurationSeconds = frameDurationSeconds;
            _customIconFrameIndex = 0;
            _customIconFrameTickCounter = 0;
            applyFrame(frames[0]); // 立刻生效，不用等下一个 tick

            ApplyCustomIconActionAnimation(selectedAction?.Animation);
        }

        // 动作专属的移动方式：override 为 null（"动作 0"，或者这个动作没写自己的 animation）就落回
        // icon 顶层那份，效果跟这个字段加进来之前完全一样。只有真的换了不一样的 animation（按引用比较
        // _customIconActiveAnimationOverride）才会 Reset+重新 Start 一遍——不然每次点击（哪怕点到的
        // 是一个没自定义 animation 的动作）都会让正在播的 sway/pulse 从头跳一下重新开始，观感很糟。
        //
        // 不用管 drift/fall 这两个分支：CustomThemeValidator 已经保证"icon 顶层选了 drift/fall 的话，
        // 任何动作都不能再单独指定 animation"，所以这里能拿到的 effective animation（不管来自动作
        // 覆盖还是顶层兜底）永远不会是 drift/fall——真出现（比如未来校验漏了什么）也只是安静地不做事，
        // 不会崩，也不会误把 CustomIcon 这几个""普通"分支专属元素当成 drift/fall 分支来用。
        private void ApplyCustomIconActionAnimation(CustomThemeAnimation? actionAnimation)
        {
            if (actionAnimation == _customIconActiveAnimationOverride) return; // 同一份（都是 null，或者同一个动作再点一次绕回来），什么都不用变
            _customIconActiveAnimationOverride = actionAnimation;

            if (_customTheme is not { } theme) return;
            var animation = actionAnimation ?? theme.Animation;
            if (string.IsNullOrWhiteSpace(animation?.Type)) return; // 顶层 animation.type 理论上校验早保证过必填，这里只是防御

            string[] animTypes = CustomThemeValidator.SplitAnimationTypes(animation.Type!);
            if (animTypes.Length == 0 || animTypes[0] is "drift" or "fall") return;

            CustomThemeColorInterop.TryParseHexColor(theme.Colors?.Accent ?? "#FFFFFF", out var accent);
            bool musicReactive = animation.MusicReactive && _settings.SkinAudioReactiveEnabled;
            double sensitivity = CustomThemeValidator.SensitivityToMultiplier(animation.Sensitivity);

            ResetCustomIconAnimationState(accent);
            foreach (var t in animTypes) StartCustomIconAnimation(t, animation.Duration, musicReactive, sensitivity);
        }

        // theme.layers（可选，最多 2 个）：每层自己的图标 + 动画，贴在卡片四个角之一，叠加在主图标/主动画
        // 之上。不像主图标那样有预先声明好的 XAML 元素可用（层数是可变的），这里运行时现造 Image + 变换 +
        // 发光效果，四角定位靠 HorizontalAlignment/VerticalAlignment + Margin，不用去猜卡片的像素尺寸。
        private void ApplyCustomExtraLayers(CustomTheme theme, Color accent)
        {
            CustomExtraLayersHost.Children.Clear();
            if (theme.Layers == null) return;

            foreach (var layer in theme.Layers)
            {
                // 层目前不支持逐帧动画（见 CustomTheme.cs 里 icon.frames 的范围说明），但校验没有单独
                // 挡住"层的 icon 里写了 frames"这种情况（ValidateIcon 是主图标/层共用的同一套规则）——
                // 用 BuildCustomIconFrames 取第一帧当静态图，跟主图标之外那几处（Mini 小方块/drift/fall/
                // 分享卡片）是同一个退化策略，不会因为用户在层里写了 frames 就直接崩
                var bitmap = CustomThemeColorInterop.BuildCustomIconFrames(layer.Icon!)[0];

                var rotate = new RotateTransform();
                var translate = new TranslateTransform();
                var glow = new DropShadowEffect { Color = accent, ShadowDepth = 0, BlurRadius = 8, Opacity = 0.6 };

                var image = new Image
                {
                    Width = 26,
                    Height = 26,
                    Stretch = Stretch.Uniform,
                    Source = bitmap,
                    RenderTransform = new TransformGroup { Children = { rotate, translate } },
                    RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                    Effect = glow,
                };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                ApplyLayerAnchor(image, layer.Anchor);
                CustomExtraLayersHost.Children.Add(image);

                bool musicReactive = layer.Animation!.MusicReactive && _settings.SkinAudioReactiveEnabled;
                double sensitivity = CustomThemeValidator.SensitivityToMultiplier(layer.Animation.Sensitivity);
                // 层里的招式组合不受"drift/fall 必须单独出现"那条限制（层没有主图标那个三图标专属轨道），
                // 8 招随便怎么组合都走同一套单图标渲染，直接全部循环应用
                foreach (var t in CustomThemeValidator.SplitAnimationTypes(layer.Animation!.Type!))
                {
                    StartLayerAnimation(t, layer.Animation.Duration, image, rotate, translate, glow, musicReactive, sensitivity);
                }
            }
        }

        // 校验已经保证 anchor 是 ValidAnchors 四选一，这里的 default 分支只是兜底，理论上到不了
        private static void ApplyLayerAnchor(FrameworkElement element, string? anchor)
        {
            element.Margin = new Thickness(10);
            switch (anchor?.ToLowerInvariant())
            {
                case "top-left":
                    element.HorizontalAlignment = HorizontalAlignment.Left;
                    element.VerticalAlignment = VerticalAlignment.Top;
                    break;
                case "top-right":
                    element.HorizontalAlignment = HorizontalAlignment.Right;
                    element.VerticalAlignment = VerticalAlignment.Top;
                    break;
                case "bottom-left":
                    element.HorizontalAlignment = HorizontalAlignment.Left;
                    element.VerticalAlignment = VerticalAlignment.Bottom;
                    break;
                default: // "bottom-right" 以及任何意外值
                    element.HorizontalAlignment = HorizontalAlignment.Right;
                    element.VerticalAlignment = VerticalAlignment.Bottom;
                    break;
            }
        }

        // 额外层的 8 招式，跟主图标 StartCustomIconAnimation 是同一套参数（保证观感一致），只是作用目标
        // 从固定的 XAML 命名元素换成运行时传进来的实例。drift/fall 在主图标那边各自有一条"飘过/飘落整张
        // 卡片"的专属轨道（CustomDriftOverlay/CustomFallOverlay），额外层没有那一套坐标系统，
        // 退化成原地小幅摆动——drift 落在水平位移，fall 复用 StartBobAnimation 但幅度更大一点，
        // 至少保留"横着晃 vs 竖着晃"这点方向感上的区别，不是完全和 sway/bob 一样。
        private void StartLayerAnimation(string type, double? customDuration, Image icon, RotateTransform rotate, TranslateTransform translate, DropShadowEffect glow, bool musicReactive, double sensitivity)
        {
            switch (type)
            {
                case "pulse":
                    {
                        var anim = new DoubleAnimation(0.35, 0.75, TimeSpan.FromSeconds(SafeDuration(customDuration, 2.2)))
                        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                        if (musicReactive) BeginMusicReactiveAnimation(glow, DropShadowEffect.OpacityProperty, anim, sensitivity);
                        else glow.BeginAnimation(DropShadowEffect.OpacityProperty, anim);
                        break;
                    }
                case "twinkle":
                    {
                        var anim = new DoubleAnimation(0.25, 1.0, TimeSpan.FromSeconds(SafeDuration(customDuration, 1.6)))
                        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                        if (musicReactive) BeginMusicReactiveAnimation(icon, UIElement.OpacityProperty, anim, sensitivity);
                        else icon.BeginAnimation(UIElement.OpacityProperty, anim);
                        break;
                    }
                case "sway":
                    {
                        var anim = new DoubleAnimation(-8, 8, TimeSpan.FromSeconds(SafeDuration(customDuration, 3.2)))
                        {
                            AutoReverse = true,
                            RepeatBehavior = RepeatBehavior.Forever,
                            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                        };
                        if (musicReactive) BeginMusicReactiveAnimation(rotate, RotateTransform.AngleProperty, anim, sensitivity);
                        else rotate.BeginAnimation(RotateTransform.AngleProperty, anim);
                        break;
                    }
                case "spin":
                    {
                        var anim = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(SafeDuration(customDuration, 4)))
                        { RepeatBehavior = RepeatBehavior.Forever };
                        if (musicReactive) BeginMusicReactiveAnimation(rotate, RotateTransform.AngleProperty, anim, sensitivity);
                        else rotate.BeginAnimation(RotateTransform.AngleProperty, anim);
                        break;
                    }
                case "flicker":
                    {
                        double dur = SafeDuration(customDuration, 2.0);
                        var frames = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
                        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.5, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.75, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(dur * 0.15))));
                        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.4, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(dur * 0.3))));
                        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.7, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(dur * 0.42))));
                        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.5, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(dur))));
                        if (musicReactive) BeginMusicReactiveAnimation(glow, DropShadowEffect.OpacityProperty, frames, sensitivity);
                        else glow.BeginAnimation(DropShadowEffect.OpacityProperty, frames);
                        break;
                    }
                case "drift":
                    {
                        var anim = new DoubleAnimation(-10, 10, TimeSpan.FromSeconds(SafeDuration(customDuration, 4)))
                        {
                            AutoReverse = true,
                            RepeatBehavior = RepeatBehavior.Forever,
                            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                        };
                        if (musicReactive) BeginMusicReactiveAnimation(translate, TranslateTransform.XProperty, anim, sensitivity);
                        else translate.BeginAnimation(TranslateTransform.XProperty, anim);
                        break;
                    }
                case "fall":
                    StartBobAnimation(translate, customDuration ?? 4, 10, musicReactive, sensitivity);
                    break;
                case "bob":
                    StartBobAnimation(translate, customDuration ?? 3, 6, musicReactive, sensitivity);
                    break;
            }
        }

        // 把一个连续循环（RepeatBehavior.Forever）的 Timeline 包成 isControllable 的 Storyboard，
        // 注册进 _musicReactiveStoryboards——这样不管是这里现场拼出来的客制化主题动画，还是 Steve 走路，
        // 都能被 UpdateMusicReactiveSkin 统一用同一套 SpeedRatio 循环调速，不用为每种情况单独写一份
        // "怎么调速"的逻辑。target 直接传对象引用（Transform/Effect 这些 Freezable），不需要它在
        // 可视化树里有名字——Storyboard.SetTarget 支持直接给对象引用。
        // sensitivityMultiplier 默认 1.0（内置皮肤/Steve 走路都不传，保持原来的反应强度），
        // 客制化主题会传 CustomThemeValidator.SensitivityToMultiplier(theme.Animation.Sensitivity) 的结果。
        private void BeginMusicReactiveAnimation(DependencyObject target, DependencyProperty property, Timeline animation, double sensitivityMultiplier = 1.0)
        {
            var storyboard = new Storyboard();
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, new PropertyPath(property));
            storyboard.Children.Add(animation);
            storyboard.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
            _musicReactiveStoryboards.Add(new MusicReactiveEntry(storyboard, sensitivityMultiplier));
        }

        // "飘过型"：同一个图标横向飘过整张卡片，飘到头瞬间重置回最左边——
        // 跟 Cloud/Rain/雪花那几个内置皮肤用的是同一套手法
        private void StartCustomDriftAnimation(TranslateTransform transform, double durationSeconds, double beginDelaySeconds, bool musicReactive, double sensitivity = 1.0)
        {
            var anim = new DoubleAnimation
            {
                From = -30,
                To = 330,
                Duration = TimeSpan.FromSeconds(SafeDuration(durationSeconds, 14)),
                BeginTime = TimeSpan.FromSeconds(beginDelaySeconds),
                RepeatBehavior = RepeatBehavior.Forever,
            };
            if (musicReactive) BeginMusicReactiveAnimation(transform, TranslateTransform.XProperty, anim, sensitivity);
            else transform.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        // "飘落型"：同一个图标从卡片顶部飘到底部，飘到头瞬间重置回最上面，叠加一点左右轻摆（比纯直线
        // 下落更自然）——跟内置的樱花/极光雪花那几个皮肤是同一套手法。摇摆的时长故意比下落短很多
        // （下落时长的 1/3.5），来回摆好几下才落地一次，摆动感才看得出来，不会显得像在匀速平移。
        private void StartCustomFallAnimation(TranslateTransform transform, double durationSeconds, double beginDelaySeconds, double swayFrom, double swayTo, bool musicReactive, double sensitivity = 1.0)
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
            var swayAnim = new DoubleAnimation
            {
                From = swayFrom,
                To = swayTo,
                Duration = TimeSpan.FromSeconds(fallSeconds / 3.5),
                BeginTime = TimeSpan.FromSeconds(beginDelaySeconds),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
            };

            if (musicReactive)
            {
                // 下落 + 摇摆是两个独立属性（Y/X），各自包一个 Storyboard——两个都跟着同一个 SpeedRatio 走，
                // 下落变快的同时摇摆也跟着变快，视觉上还是同步的，不会看着像两套节奏打架
                BeginMusicReactiveAnimation(transform, TranslateTransform.YProperty, fallAnim, sensitivity);
                BeginMusicReactiveAnimation(transform, TranslateTransform.XProperty, swayAnim, sensitivity);
            }
            else
            {
                transform.BeginAnimation(TranslateTransform.YProperty, fallAnim);
                transform.BeginAnimation(TranslateTransform.XProperty, swayAnim);
            }
        }

        // "浮动型"（bob）：纯粹的位置上下浮动，缓入缓出——跟 sway（绕轴心转角度摆动）是两个不同的动作。
        // 海边黄昏的帆船停在海面上、云朵漂浮的热气球飘在天上、极光雪夜的北极狐/雨夜的窗台猫，都用这同一个
        // 方法，只是幅度/时长不同；这个动作也加进了客制化主题的第 8 种可选招式，见 StartCustomIconAnimation
        // 里的 "bob" 分支。musicReactive 默认 false——目前只有客制化主题会传 true，内置皮肤里用 bob 的
        // 这几套（云朵/海边黄昏/极光雪夜/雨夜）故意不用同一个开关接进音乐律动，各自另有自己的律动落点
        // （见 MainWindow.SkinInteractions.cs 的对照表），不然一个方法改了所有调用方都跟着变，不好控制范围
        private void StartBobAnimation(TranslateTransform transform, double durationSeconds, double amplitude, bool musicReactive = false, double sensitivity = 1.0)
        {
            var anim = new DoubleAnimation(-amplitude, amplitude, TimeSpan.FromSeconds(SafeDuration(durationSeconds, 3)))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            if (musicReactive) BeginMusicReactiveAnimation(transform, TranslateTransform.YProperty, anim, sensitivity);
            else transform.BeginAnimation(TranslateTransform.YProperty, anim);
        }

        // CustomThemeValidator 已经要求 duration 必须 > 0，但这里再兜底一层：万一是加校验之前存的老文件
        // （或者以后校验逻辑本身有疏漏），也不会因为 0/负数时长让 WPF 的动画系统直接抛异常崩掉整个 App。
        private static double SafeDuration(double? value, double fallback) =>
            value.HasValue && value.Value > 0 ? value.Value : fallback;

        // 基准姿态：角度归零、图标不透明、发光用皮肤强调色——调用方（ApplyCustomSkinVisuals）在整个
        // 招式组合循环开始之前调一次，不要挪进 StartCustomIconAnimation 里，不然组合里后一招重置的时候
        // 会把前一招刚设好的状态擦掉
        private void ResetCustomIconAnimationState(Color accent)
        {
            CustomIconRotate.Angle = 0;
            CustomIcon.Opacity = 1;
            CustomIconGlow.Color = accent;
            CustomIconGlow.Opacity = 0.6;
        }

        // 单图标动画："招式"从 pulse / twinkle / drift / fall / bob / sway / spin / flicker 里选一个或者
        // 用 + 组合几个（组合规则见 CustomThemeValidator.ValidateAnimation），全部用代码现场构造
        // DoubleAnimation，不需要预先在 XAML 里声明 Storyboard 资源。musicReactive 为 true 时，
        // 不管选的是哪一招，都统一走 BeginMusicReactiveAnimation 包成可调速的 Storyboard——
        // 见 ApplyCustomSkinVisuals 里对 "跟着音乐律动" 开关的说明
        private void StartCustomIconAnimation(string type, double? customDuration, bool musicReactive, double sensitivity = 1.0)
        {
            switch (type)
            {
                case "pulse": // 呼吸发光，平缓的明暗循环
                    {
                        var anim = new DoubleAnimation(0.35, 0.75, TimeSpan.FromSeconds(SafeDuration(customDuration, 2.2)))
                        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                        if (musicReactive) BeginMusicReactiveAnimation(CustomIconGlow, DropShadowEffect.OpacityProperty, anim, sensitivity);
                        else CustomIconGlow.BeginAnimation(DropShadowEffect.OpacityProperty, anim);
                        break;
                    }
                case "twinkle": // 图标本体渐隐渐现，比 pulse 幅度更大、更像星星闪烁
                    {
                        var anim = new DoubleAnimation(0.25, 1.0, TimeSpan.FromSeconds(SafeDuration(customDuration, 1.6)))
                        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                        if (musicReactive) BeginMusicReactiveAnimation(CustomIcon, UIElement.OpacityProperty, anim, sensitivity);
                        else CustomIcon.BeginAnimation(UIElement.OpacityProperty, anim);
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
                        if (musicReactive) BeginMusicReactiveAnimation(CustomIconRotate, RotateTransform.AngleProperty, anim, sensitivity);
                        else CustomIconRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
                        break;
                    }
                case "spin": // 匀速转圈，跟黑胶/磁带卷盘同一套手法
                    {
                        var anim = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(SafeDuration(customDuration, 4)))
                        { RepeatBehavior = RepeatBehavior.Forever };
                        if (musicReactive) BeginMusicReactiveAnimation(CustomIconRotate, RotateTransform.AngleProperty, anim, sensitivity);
                        else CustomIconRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
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
                        if (musicReactive) BeginMusicReactiveAnimation(CustomIconGlow, DropShadowEffect.OpacityProperty, frames, sensitivity);
                        else CustomIconGlow.BeginAnimation(DropShadowEffect.OpacityProperty, frames);
                        break;
                    }
                case "bob": // 上下轻轻浮动，跟海边黄昏的帆船、云朵漂浮的热气球同一套手法——纯位置浮动，
                            // 不是旋转摆动（那是 sway），振幅比内置那两个稍大一点，客制化图标通常比装饰物更显眼一些
                    StartBobAnimation(CustomIconBob, customDuration ?? 3, 5, musicReactive, sensitivity);
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
            UpdateBilingualToggleIcon();

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

        // Steve 来回走：范围按窗口实际宽度算，不再是写死的 20~420（小尺寸会走出界，大尺寸又用不满）。
        // musicReactive 为 true 时把左右移动这两个动画包成可调速的 Storyboard，走路节奏跟着音乐变——
        // 换腿的节奏（UpdateSteveWalkAnimation 里的 _steveLegTickCounter）也会跟着同一个比例变快/变慢
        private void StartSteveWalking(bool musicReactive)
        {
            double rightBound = Math.Max(60, Width - 110); // 110 = 两侧 Canvas 内边距 + 预留给树的位置
            var duration = TimeSpan.FromSeconds(7);

            var xAnim = new DoubleAnimation(20, rightBound, duration) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            var shadowAnim = new DoubleAnimation(20, rightBound, duration) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };

            if (musicReactive)
            {
                BeginMusicReactiveAnimation(SteveTransform, TranslateTransform.XProperty, xAnim);
                BeginMusicReactiveAnimation(ShadowTransform, TranslateTransform.XProperty, shadowAnim);
            }
            else
            {
                SteveTransform.BeginAnimation(TranslateTransform.XProperty, xAnim);
                ShadowTransform.BeginAnimation(TranslateTransform.XProperty, shadowAnim);
            }

            _steveLastX = 20;
        }

        // UFO 飞过夜空：范围按窗口实际宽度算，跟上面 Steve 走路是同一个道理，不写死避免小窗口飞出界、
        // 大窗口又飞不满；两头用缓动而不是匀速，看起来更像真的在飞而不是机械平移
        private void StartUfoDrift()
        {
            double rightBound = Math.Max(60, Width - 70); // 70 = 两侧内边距 + UFO 自身宽度，留够飞完整趟的空间
            var anim = new DoubleAnimation(0, rightBound, TimeSpan.FromSeconds(9))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            UfoDriftTransform.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        // 两只小怪物同步"踏步"横移：用离散关键帧（DiscreteDoubleKeyFrame）而不是普通的连续插值动画，
        // 数值是一帧一帧硬跳过去的，不会有过渡的滑动感——这才是原作那种逐帧位移的机械感，用连续动画
        // 会显得像在"滑冰"而不是"走路"。两只怪物共用同一个 Timeline 对象（BeginAnimation 内部会各自
        // 生成独立的 Clock），从同一刻开始播，天然就是同步的。
        private void StartInvadersMarch()
        {
            var frames = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(3, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.5))));
            frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.0))));
            frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(-3, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5))));
            frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.0))));

            Invader1MarchTransform.BeginAnimation(TranslateTransform.XProperty, frames);
            Invader2MarchTransform.BeginAnimation(TranslateTransform.XProperty, frames);
        }

        // 地铁车厢驶过：范围按窗口实际宽度算，跟 UFO/Steve 是同一个道理。故意不用 AutoReverse——
        // 地铁开过去就该消失、下一轮从头再开一次，不是倒车开回来；也不用缓动，车开得匀速更像地铁，
        // 不是飘忽不定的 UFO。
        private void StartTrainDrift()
        {
            double rightBound = Math.Max(60, Width - 100); // 100 = 两侧内边距 + 车厢自身宽度
            var anim = new DoubleAnimation(0, rightBound, TimeSpan.FromSeconds(7))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            };
            TrainDriftTransform.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        // 每 tick 检查一次：走路换腿（约 250ms 一帧）+ 按位移方向翻面朝向。
        // 音乐律动开着的时候，换腿节奏按 _steveWalkSpeedRatio（UpdateMusicReactiveSkin 里算出来的同一个
        // 播放速度倍率）缩放——腿走得快的同时也换得快，两边看起来是同一件事在提速，不会腿慢身体快
        private void UpdateSteveWalkAnimation()
        {
            double currentX = SteveTransform.X;

            if (currentX > _steveLastX + 0.05) SteveFlip.ScaleX = 1;   // 在往右走，面朝右
            else if (currentX < _steveLastX - 0.05) SteveFlip.ScaleX = -1; // 在往左走，翻转面朝左
            _steveLastX = currentX;

            _steveLegTickCounter++;
            bool walkIsReactive = _isMusicReactiveSkin && _settings.SkinAudioReactiveEnabled;
            int legSwapTicks = walkIsReactive ? Math.Max(1, (int)Math.Round(5 / _steveWalkSpeedRatio)) : 5; // 5 × 50ms ≈ 250ms 一帧
            if (_steveLegTickCounter >= legSwapTicks)
            {
                _steveLegTickCounter = 0;
                ImgSteve.Source = ImgSteve.Source == _steveFrame1 ? _steveFrame2 : _steveFrame1;
            }
        }

        // 客制化主题主图标的逐帧动画——跟上面 UpdateSteveWalkAnimation 是同一套"计时器数 tick、攒够就
        // 换一帧"机制，只是从写死两张图变成用户 JSON 里任意张（见 CustomTheme.cs 的 CustomThemeIcon.
        // Frames）。musicReactive 开着的话换帧节奏按 _customIconFrameSpeedRatio（UpdateMusicReactiveSkin
        // 里算出来的同一份播放速度倍率，经过这份主题自己的 sensitivity 调整过）缩放，跟 Steve 换腿变速
        // 是同一个道理；没开的话就是 frameDuration 写多少就多快，雷打不动。
        //
        // 调用方（SmoothTimer_Tick）已经拿 `_customIconFrames != null` 判断过要不要调这个方法，这里
        // 不重复判断；_customIconFrames.Length 理论上一定 > 1（见 ApplyCustomSkinVisuals 只在 >1 帧时
        // 才赋值），但还是留一道防御，免得以后有别的地方误赋值成单帧数组时这里直接除零/数组越界。
        private void UpdateCustomIconFrameAnimation()
        {
            var frames = _customIconFrames;
            if (frames == null || frames.Length <= 1) return;

            _customIconFrameTickCounter++;
            double speedRatio = _customIconFramesMusicReactive ? _customIconFrameSpeedRatio : 1.0;
            int ticksPerFrame = Math.Max(1, (int)Math.Round(_customIconFrameDurationSeconds * 1000 / 50.0 / speedRatio));
            if (_customIconFrameTickCounter < ticksPerFrame) return;

            _customIconFrameTickCounter = 0;
            _customIconFrameIndex = (_customIconFrameIndex + 1) % frames.Length;
            _customIconFrameApply?.Invoke(frames[_customIconFrameIndex]);
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
