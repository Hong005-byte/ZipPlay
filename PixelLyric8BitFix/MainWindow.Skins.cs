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

            // 每次真正应用一个客制化主题都重新走一遍——点击切换过动作的话，换皮肤/重开窗口要从
            // "动作 0"（图标自己的 Rows/Frames）重新开始，不该记着上次切到了第几个。
            // _customIconActiveAnimationOverride 重置成专门的""还没初始化过""哨兵（不是 null）——
            // 见 UninitializedIconAnimation 字段的注释：如果这里也直接置 null，下面
            // SetCustomIconActionIndex(0) 里""动作 0 的 animation 本来就是 null""这次调用会被误判成
            // ""跟上次一样，什么都不用做""，图标会维持在 ApplySkin 清空时设的全部 Collapsed，什么都
            // 不显示。
            _customIconActionIndex = 0;
            _customIconFrames = null;
            _customIconFrameApply = null;
            _customIconActiveAnimationOverride = UninitializedIconAnimation;
            _customIconAutoSwitchHighWaterMark = 0; // 换皮肤/重开窗口，"到过多远"这个进度也该归零，不是只有换歌才清

            // 图标该待在哪条轨道（普通装饰栏 / drift 三重影 / fall 三重影）、播什么帧/动画，全部交给
            // SetCustomIconActionIndex(0) 去做——跟点击装饰图标/数据驱动自动切换走的是同一条路径，
            // 不用在这里再维护一份重复的分支逻辑，两份逻辑也不会有慢慢走岔的风险
            SetCustomIconActionIndex(0);

            ApplyCustomExtraLayers(theme, accent);
        }

        // 点击装饰图标（CustomIcon_MouseLeftButtonDown）触发：循环切到下一个 icon.actions，绕完一圈
        // 回到"动作 0"。数据驱动的自动切换（EvaluateAutoSwitchIconAction）用的是同一套"切到第 index
        // 个动作"逻辑，两条触发路径共用 SetCustomIconActionIndex，不会各自维护一份走岔。
        private void CycleCustomIconAction()
        {
            if (_customTheme?.Icon is not { } icon) return;
            var actions = icon.Actions ?? new List<CustomThemeIconAction>();
            SetCustomIconActionIndex((_customIconActionIndex + 1) % (actions.Count + 1));
        }

        // 把图标真正切到第 index 个动作（0 = 图标自己的 Rows/Frames，"动作 0"；>0 对应
        // theme.Icon.Actions[index-1]）：先让 ApplyCustomIconMovement 决定这个动作该用哪条移动轨道
        // （普通装饰栏 / drift 三重影 / fall 三重影——如果这个动作自己带了 animation 就可能真的换轨道，
        // 见该方法注释），这一步必须在算帧/应用帧之前做，因为它会顺带把 _customIconFrameApply 指向
        // 新轨道该画帧的地方；不然帧会画去旧轨道已经隐藏起来的元素上。BuildCustomIconFrames 本来就是
        // 纯粹"数据转位图"，借同一份 Palette 换一套 Frames 现造一个 CustomThemeIcon 传进去，
        // 不用改这个方法一行。
        private void SetCustomIconActionIndex(int index)
        {
            if (_customTheme?.Icon is not { } icon) return;
            var actions = icon.Actions ?? new List<CustomThemeIconAction>();
            if (index < 0 || index > actions.Count) return; // 越界的话什么都不做，比如主题被换掉之后动作数量变少，旧索引不再有效

            _customIconActionIndex = index;
            // 不管这次切换是点击触发的还是自动切换触发的，都顺手把"曾经到过的最远动作"往前推——
            // 只推不退（Math.Max）。EvaluateAutoSwitchIconAction 靠这个字段（而不是
            // _customIconActionIndex 本身）判断"要不要自动切"，见那边的注释——这是修复"手动点回去
            // 之后马上被自动切换弹回来"那个 bug 的关键。
            _customIconAutoSwitchHighWaterMark = Math.Max(_customIconAutoSwitchHighWaterMark, index);
            CustomThemeIconAction? selectedAction = index == 0 ? null : actions[index - 1];

            ApplyCustomIconMovement(selectedAction?.Animation);
            if (_customIconFrameApply is not { } applyFrame) return; // 理论上上面跑完一定有值，这里只是防御

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
        }

        // 数据驱动的自动切换：当前歌曲"连续播放"（_customIconContinuousTrackSeconds，见 MainWindow.
        // ListeningStats.cs——暂停不计时，切歌清零）满某个动作设定的 autoSwitchAfterSeconds 秒数，
        // 就自动切过去，不用等用户点。每个播放 tick（UpdateListeningStats）都会调一次。
        //
        // 关键点：拿去跟阈值比较、判断"要不要自动切"的是 _customIconAutoSwitchHighWaterMark（曾经
        // 到过的最远动作），不是 _customIconActionIndex（当前正显示的动作）——这两个字段刻意分开。
        // 用户点击可以把 _customIconActionIndex 改成任何值（包括比 HighWaterMark 更靠前的），但
        // HighWaterMark 只会在 SetCustomIconActionIndex 里被 Math.Max 往前推，不会被点击拉低。
        //
        // 如果这里跟之前一样直接拿 _customIconActionIndex 当基准：用户手动点回一个更早的动作之后，
        // 下一个 50ms tick（这个方法每 tick 都跑一次）会立刻重新算出"当前连续播放时长早就该在
        // 更靠后的动作"，把用户刚点回去的选择弹回去——因为只有 50ms，用户根本看不出点击生效过，
        // 感觉就是"点了跟没点一样，换不到"。改成跟 HighWaterMark 比较之后：只要这次算出来的阶段
        // 没有超过""曾经到过的最远""，就什么都不做，用户点哪就停在哪，一直停到真的有一个新的、
        // 从没到过的阈值被跨过为止——那时候才应该重新推进，这也是为什么不能干脆"点了以后永远不再
        // 自动切"：后面几个阈值仍然应该按时触发，不能因为用户点过一次就整个失效。
        private void EvaluateAutoSwitchIconAction()
        {
            if (_customTheme?.Icon?.Actions is not { Count: > 0 } actions) return;

            int desiredIndex = CustomThemeIconActionAutoSwitch.GetDesiredActionIndex(actions, _customIconContinuousTrackSeconds, _customIconAutoSwitchHighWaterMark);
            if (desiredIndex > _customIconAutoSwitchHighWaterMark) SetCustomIconActionIndex(desiredIndex);
        }

        // 换歌那一刻调用（MainWindow.Lyrics.cs 的 HandleTrackChangeAsync）：连续播放计时器 +
        // HighWaterMark 一起清零——"连续听同一首歌多久""到过多远的阶段"这两件事本来就该随着换歌
        // 重新计起，不该带着上一首歌攒的进度。
        //
        // 只有这份主题真的有任何一个动作配了 autoSwitchAfterSeconds，才会顺带把姿势拉回"动作 0"
        // 重新开始——没配这个字段的主题（绝大多数）完全不受影响，换歌不会打断用户手动点选的姿势，
        // 跟这个功能加进来之前一模一样。真配了的主题才需要这个重置：不然新歌一开始，图标可能还顶着
        // 上一首歌攒出来的"投入很久"那个姿势，跟新歌的实际播放时长对不上。
        private void ResetCustomIconAutoSwitchTrackState()
        {
            _customIconContinuousTrackSeconds = 0;
            if (_customTheme?.Icon?.Actions is { Count: > 0 } actions && actions.Any(a => a.AutoSwitchAfterSeconds is > 0))
            {
                _customIconAutoSwitchHighWaterMark = 0; // 显式清零——SetCustomIconActionIndex(0) 自己只会 Math.Max，不会把这个字段往回拉
                SetCustomIconActionIndex(0);
            }
        }

        // 决定图标现在该待在哪条移动轨道上（普通装饰栏 / drift 三重影 / fall 三重影 / walk 来回走），
        // 把对应动画启动好，并且把 _customIconFrameApply 指向这条轨道该画帧的地方。actionAnimation
        // 为 null 就是"动作 0"，或者这个动作没写自己的 animation，落回 icon 顶层那份——效果跟每个动作
        // 只能换帧、不能换动法的那个阶段完全一样。填了的话（包括 drift/fall/walk）就用这份，图标真的
        // 会搬进/搬出对应的专属轨道。walk 跟 Steve/火车走的是同一条装饰带（CustomIconDecorCanvas，
        // Grid.Row="0" + ZIndex=10），单张图标在两个边界之间来回摆，不是 drift/fall 那种三重影飘过
        // 整张卡片——这是应用户明确要求补的："跟 Steve/火车同理"的走路方式，drift/fall 的""飘""不是
        // 同一回事。
        //
        // 主题刚应用（ApplyCustomSkinVisuals 调 SetCustomIconActionIndex(0)）和之后每次切动作（点击/
        // 数据驱动自动切换）都走这一个方法，不会有两份分支逻辑走岔的风险。只有真的换了不一样的
        // animation（按引用比较 _customIconActiveAnimationOverride）才会重新摆一遍轨道——不然每次
        // 点击（哪怕点到的是一个没自定义 animation 的动作）都会让正在播的动画从头跳一下重新开始，
        // 观感很糟；同一份没变的话，_customIconFrameApply 也保持不动，帧照样画在原来那条轨道上。
        //
        // 旧轨道的动画不主动停：切走之后对应的容器被隐藏，动画效果看不见，纯粹是省不出来一点点
        // CPU——跟这个 app 别的地方对这类"看不见的动画还在偷偷转"的容忍度一致（ApplySkin 里
        // _musicReactiveStoryboards.Clear() 那段注释也承认过同一类"浪费但不出错"的取舍）。点击/
        // 自动切换都是低频事件（人手速，或者最多几个播放阈值），不是每帧都会触发，攒下来的这点空转
        // 开销可以忽略，换来的是不用给每一种招式的 Start*Animation 方法都补一段"怎么精确撤销自己"的
        // 对称逻辑——那部分改动面更大，也是这个环境完全没法用眼睛验证效果的地方，犯不上为了省这点
        // 开销冒险引入新 bug。
        private void ApplyCustomIconMovement(CustomThemeAnimation? actionAnimation)
        {
            if (actionAnimation == _customIconActiveAnimationOverride) return; // 同一份（都是 null，或者同一个动作再点一次绕回来），什么都不用变
            _customIconActiveAnimationOverride = actionAnimation;

            if (_customTheme is not { Icon: { } icon } theme) return;
            var animation = actionAnimation ?? theme.Animation;
            if (string.IsNullOrWhiteSpace(animation?.Type)) return; // 顶层 animation.type 理论上校验早保证过必填，这里只是防御

            string[] animTypes = CustomThemeValidator.SplitAnimationTypes(animation.Type!);
            if (animTypes.Length == 0) return;

            bool hasActions = icon.Actions is { Count: > 0 };
            CustomThemeColorInterop.TryParseHexColor(theme.Colors?.Accent ?? "#FFFFFF", out var accent);
            bool musicReactive = animation.MusicReactive && _settings.SkinAudioReactiveEnabled;
            double sensitivity = CustomThemeValidator.SensitivityToMultiplier(animation.Sensitivity);
            double? customDuration = animation.Duration;

            // 逐帧动画的换帧节奏是不是跟音乐反应、反应多强——这份跟着"现在生效的是哪份 animation"走，
            // 不是从第一次应用主题之后就再也不变（这是这轮顺手修的一个小疏漏：之前动作专属 animation
            // 上线时漏了同步这三个字段，只有主题刚应用那一刻的顶层 musicReactive/sensitivity 会生效，
            // 切到一个自己开了/关了音乐律动的动作，换帧节奏并不会跟着变）
            _customIconFramesMusicReactive = musicReactive;
            _customIconFrameSensitivity = sensitivity;
            _customIconFrameSpeedRatio = 1.0;

            // 先把四条轨道都摆成"没有被选中"的状态，再挑一条真正启用——不去比较"上一次是哪条"，
            // 每次都是确定状态，逻辑更简单也不容易漏掉某个分支的清理。CustomIcon/CustomWalkIcon 都在
            // 同一个 CustomIconDecorCanvas 里（普通招式 vs walk 二选一显示），所以这两个的 Visibility
            // 单独摆，不跟着 CustomIconDecorCanvas 本身的显隐绑在一起
            RowDecor.Height = new GridLength(0);
            CustomIconDecorCanvas.Visibility = Visibility.Collapsed;
            CustomIcon.Visibility = Visibility.Collapsed;
            CustomWalkIcon.Visibility = Visibility.Collapsed;
            CustomDriftOverlay.Visibility = Visibility.Collapsed;
            CustomFallOverlay.Visibility = Visibility.Collapsed;

            if (animTypes[0] == "drift")
            {
                CustomDriftOverlay.Visibility = Visibility.Visible;
                CustomDriftIcon1.Cursor = CustomDriftIcon2.Cursor = CustomDriftIcon3.Cursor = hasActions ? Cursors.Hand : Cursors.Arrow;
                StartCustomDriftAnimation(CustomDrift1Transform, customDuration ?? 14, 0, musicReactive, sensitivity);
                StartCustomDriftAnimation(CustomDrift2Transform, (customDuration ?? 14) * 1.35, 2, musicReactive, sensitivity);
                StartCustomDriftAnimation(CustomDrift3Transform, (customDuration ?? 14) * 1.7, 5, musicReactive, sensitivity);
                _customIconFrameApply = bmp => CustomDriftIcon1.Source = CustomDriftIcon2.Source = CustomDriftIcon3.Source = bmp;
            }
            else if (animTypes[0] == "fall")
            {
                CustomFallOverlay.Visibility = Visibility.Visible;
                CustomFallIcon1.Cursor = CustomFallIcon2.Cursor = CustomFallIcon3.Cursor = hasActions ? Cursors.Hand : Cursors.Arrow;
                StartCustomFallAnimation(CustomFall1Transform, customDuration ?? 6, 0, -6, 8, musicReactive, sensitivity);
                StartCustomFallAnimation(CustomFall2Transform, (customDuration ?? 6) * 1.3, 1.5, 4, -10, musicReactive, sensitivity);
                StartCustomFallAnimation(CustomFall3Transform, (customDuration ?? 6) * 1.6, 3, -8, 6, musicReactive, sensitivity);
                _customIconFrameApply = bmp => CustomFallIcon1.Source = CustomFallIcon2.Source = CustomFallIcon3.Source = bmp;
            }
            else if (animTypes[0] == "walk")
            {
                // 跟 Steve/火车同一条装饰带（CustomIconDecorCanvas 本身就是 Grid.Row="0" + ZIndex=10，
                // 跟 MinecraftDecorCanvas/CityDecorCanvas 同一层），不会被卡片下面的歌词内容挡住
                RowDecor.Height = new GridLength(50);
                CustomIconDecorCanvas.Visibility = Visibility.Visible;
                CustomWalkIcon.Visibility = Visibility.Visible;
                CustomWalkIcon.Cursor = hasActions ? Cursors.Hand : Cursors.Arrow;
                StartCustomWalkAnimation(customDuration, musicReactive, sensitivity);
                _customIconFrameApply = bmp => CustomWalkIcon.Source = bmp;
            }
            else
            {
                RowDecor.Height = new GridLength(50);
                CustomIconDecorCanvas.Visibility = Visibility.Visible;
                CustomIcon.Visibility = Visibility.Visible;
                CustomIcon.Cursor = hasActions ? Cursors.Hand : Cursors.Arrow;
                // 重置放在循环外面，只做一次——挪进循环里的话，组合里后一招重置的时候会把前一招刚设好的
                // 状态（比如 sway 已经在转的角度）擦掉，等于每加一招都在跟前面打架
                ResetCustomIconAnimationState(accent);
                foreach (var t in animTypes) StartCustomIconAnimation(t, customDuration, musicReactive, sensitivity);
                _customIconFrameApply = bmp => CustomIcon.Source = bmp;
            }
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

        // 额外层的 9 招式，跟主图标 StartCustomIconAnimation 是同一套参数（保证观感一致），只是作用目标
        // 从固定的 XAML 命名元素换成运行时传进来的实例。drift/fall/walk 在主图标那边各自有专属的渲染
        // 结构（CustomDriftOverlay/CustomFallOverlay 三重影轨道，walk 是装饰带里来回走的单独图标），
        // 额外层没有那一套坐标系统/装饰带，drift 和 walk 一起退化成同一种原地水平小幅摆动，fall 复用
        // StartBobAnimation 但幅度更大一点，至少保留"横着晃 vs 竖着晃"这点方向感上的区别，不是完全
        // 和 sway/bob 一样。
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
                // walk 跟 drift 共用这同一个 case：层没有 Steve/火车那种独立装饰带可以横穿，也没有
                // drift/fall 主图标才有的专属轨道，两者在层里都只能退化成同一种"原地水平小幅摆动"，
                // 没有必要为 walk 单独再写一份几乎一样的动画
                case "drift":
                case "walk":
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

        // 客制化图标"来回走"专属（animation.type = walk）：跟 Minecraft 皮肤 Steve 走路（见下面
        // StartSteveWalking）借的是同一套"AutoReverse 往返"手法，但起点不一样——Steve 在两个固定
        // 端点之间摆，walk 是从图标平时待着的原位（CustomWalkTransform.X=0，跟 CustomIcon 同一个
        // 锚点，见 MainWindow.xaml 里 CustomWalkIcon 的注释）出发向左走，走到头再走回原位，符合
        // "图标本来待在这，只是偶尔走出去逛一圈再回来"这个直觉，而不是凭空站在别的地方来回摆。
        // leftDistance（走多远）按窗口实际宽度算，不写死，小窗口不会走出界、大窗口也走得满；
        // 70 = CustomWalkIcon 自身宽度(36) + 两侧留白，跟 UFO 那条没有额外装饰物占位的横穿动画
        // （StartUfoDrift）算法思路一样，不是照抄 Steve 的 110（那个 110 里包含了 Minecraft 皮肤专属的
        // 小树占位，客制化图标这条装饰带没有树）。
        //
        // 故意没做 Steve 那套"翻转朝向"（SteveFlip，走左边翻转成朝左）：客制化图标形状千变万化，
        // 贸然做水平镜像可能把不该翻的细节（比如带文字、明显方向性的图案）翻反，不是每个图标都适合
        // 被镜像——想要"朝左朝右换个样子"的话，用 icon.frames 自己画两帧不同朝向的图更安全、更可控。
        private void StartCustomWalkAnimation(double? customDuration, bool musicReactive, double sensitivity)
        {
            double leftDistance = Math.Max(60, Width - 70);
            var anim = new DoubleAnimation(0, -leftDistance, TimeSpan.FromSeconds(SafeDuration(customDuration, 7)))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
            };
            if (musicReactive) BeginMusicReactiveAnimation(CustomWalkTransform, TranslateTransform.XProperty, anim, sensitivity);
            else CustomWalkTransform.BeginAnimation(TranslateTransform.XProperty, anim);
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

        // 单图标动画：只处理不需要专属渲染结构的 6 招——pulse / twinkle / bob / sway / spin / flicker，
        // 可以用 + 任意组合（组合规则见 CustomThemeValidator.ValidateAnimation）。drift/fall/walk
        // 不会走到这个 switch——那三招各自需要专属的容器/图标（三重影轨道或者装饰带里的 CustomWalkIcon），
        // 在 ApplyCustomIconMovement 里就已经分流走了，见该方法。全部用代码现场构造 DoubleAnimation，
        // 不需要预先在 XAML 里声明 Storyboard 资源。musicReactive 为 true 时，不管选的是哪一招，都统一
        // 走 BeginMusicReactiveAnimation 包成可调速的 Storyboard——见 ApplyCustomIconMovement 里对
        // "跟着音乐律动" 开关的说明
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
