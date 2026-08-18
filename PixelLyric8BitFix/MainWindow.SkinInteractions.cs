using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace PixelLyric8BitFix
{
    // 两类功能，都是纯粹让皮肤"感觉是活的"，不涉及任何数据存储/账户：
    //   1. 皮肤音乐律动：黑胶/磁带机/篝火/星空/雨夜/极光雪夜/樱花/CRT/赛博朋克/复古街机/太空侵略者/
    //      都市夜景这些内置皮肤的 ambient Storyboard 转速、Minecraft 皮肤 Steve 的走路节奏和跳跃、
    //      加上客制化主题勾了 musicReactive 开关的情况，都跟着系统正在播的音乐响度/节奏实时变化
    //      （UpdateMusicReactiveSkin，每个 SmoothTimer_Tick 调一次）。lofi、烛光冥想两套刻意保持
    //      安静，不接进去。
    //   2. 装饰物可以戳一下：Steve 点一下跳一下、篝火点一下"旺"一下，纯一次性正反馈，不影响循环动画
    //
    // 律动的"动作"一共 6 种可复用的原语，不是每套皮肤各写一份专属逻辑：
    //   - SpeedRatio：ambient Storyboard 的播放速度整体变快/变慢（_musicReactiveStoryboards，逐个调）
    //   - BeatGrow：鼓点冲击瞬间放大一截，再慢慢缩回 1.0（UpdateBeatPulse 的 attack-decay）
    //   - BeatFlash：跟 BeatGrow 同一套 attack-decay 数学，只是作用在发光/透明度而不是缩放
    //   - BeatBounce：鼓点上升沿触发一次性位移弹一下（PlayBeatBounce + TryArmBeatTrigger），
    //     可以是纵向的跳跃（Steve/UFO）也可以是横向的突进（City 的地铁），传哪个轴的 DependencyProperty
    //     进去就是哪个方向
    //   - BeatShake：跟 BeatBounce 同一套上升沿检测，只是动画本体是一次性左右来回晃动（PlayBeatShake）
    //   - BeatWobble：也是同一套上升沿检测，动画本体是一次性的角度晃一下再回正（PlayBeatWobble），
    //     跟 BeatShake 的区别是"转"而不是"平移"，落在 RotateTransform 上
    public partial class MainWindow : Window
    {
        // 响度/节奏怎么映射成播放速度倍率：
        //   - 静音/几乎没声音的时候落在 BaseSpeedRatio，跟"没接音频"时的固定节奏循环感觉差不多，
        //     不会因为没声音就完全停住看起来像卡死
        //   - OverallLevel 不是直接乘一个倍数就用——实测正常听歌音量下这个值大部分时间只在
        //     0.05~0.3 之间晃（远够不到理论上限的 1.0），直接线性乘会导致速度几乎不变、看不出效果
        //     （最早那版就是这么翻车的）。这里先按 LevelReference（"大概什么值算是响"）把它重新
        //     归一化到 0~1，再映射到 [BaseSpeedRatio, LevelPeakRatio] 这个区间，响度稍微大一点
        //     就能感觉到明显提速，不用真的拉满到理论最大值
        //   - 鼓点/重音瞬间在归一化后的响度之上再叠加一截额外提速（BeatSpeedBoost），让"卡点"这件事
        //     单独看得出来，不是只有响度这一个维度在起作用
        //   - 最后夹在 [MinSpeedRatio, MaxSpeedRatio] 之间，避免偶尔的极端值转到肉眼觉得离谱的速度
        private const double BaseSpeedRatio = 0.7;
        private const double LevelReference = 0.32; // OverallLevel 达到这个值就算"响"，映射直接封顶
        private const double LevelPeakRatio = 2.0;   // 响度封顶时能到的速度倍率（还没算鼓点加成）
        private const double BeatSpeedBoost = 1.2;
        private const double MinSpeedRatio = 0.4;
        private const double MaxSpeedRatio = 2.6;

        // BeatGrow/BeatFlash 共用的"武装"阈值以外的另一套参数：鼓点冲击瞬间朝目标值跳（快 attack），
        // 没有冲击的时候按 decayPerTick 慢慢缩回 resting（慢 decay）——跟 AudioVisualizer 里 _smoothed
        // 那个 VU 表手法一样。篝火的"旺一下"、樱花树的"绽放脉冲"用在 Scale 上；雨夜/极光雪夜的
        // 发光闪一下用在 Opacity 上，同一个数学公式，落点不一样而已。
        private static double UpdateBeatPulse(double current, double resting, double boosted, float beatPulse, double beatToBoost, double decayPerTick)
        {
            double target = Math.Min(resting + beatPulse * beatToBoost, boosted);
            return target > current ? target : resting + (current - resting) * decayPerTick;
        }

        // BeatBounce/BeatShake 共用的"武装/解除武装"上升沿检测：冲过 armedThreshold 才触发一次，
        // 触发完直到衰减到 disarmThreshold 以下才重新武装，两个阈值中间隔一段距离（不是同一个值），
        // 避免数值刚好卡在临界点附近来回抖、连续触发好几次。每套皮肤各自维护自己的 armed 字段——
        // 不同皮肤是独立的触发源，不共用一个布尔。
        private static bool TryArmBeatTrigger(ref bool armed, float beatPulse, float armedThreshold, float disarmThreshold)
        {
            if (armed && beatPulse >= armedThreshold)
            {
                armed = false;
                return true;
            }
            if (!armed && beatPulse < disarmThreshold)
            {
                armed = true;
            }
            return false;
        }

        // 具体阈值是按实测的 BeatPulse 数值范围估的（大部分时间不到 0.05，"有鼓点"的时候能到
        // 0.03~0.05 这个量级），Steve 跳跃、UFO 跳一下、CRT/赛博朋克的抖动都共用同一对阈值——
        // 都是同一份 BeatPulse 数据，没理由各配一套
        private const float BeatTriggerArmedThreshold = 0.02f;
        private const float BeatTriggerDisarmThreshold = 0.008f;

        // 篝火专属：BeatGrow 落在缩放上，1.0 是静息值，复用的是戳一下篝火那个 CampfirePokeScale——
        // 点击动画播的时候会暂时接管这个属性，播完自动交还，两边不会打架（见 ImgCampfire_MouseLeftButtonDown 那边的注释）
        private const double CampfireBeatGrowAmount = 3.0;
        private const double CampfireMaxGrowScale = 1.6;
        private const double CampfireGrowDecayPerTick = 0.82;
        private double _campfireBeatGrowScale = 1.0;

        // 樱花树专属：同一套 BeatGrow，落在 SakuraTreePokeScale 上，"绽放"一下比篝火更轻一点，
        // 树本身不该像火苗那样抢戏
        private const double SakuraBeatGrowAmount = 1.6;
        private const double SakuraMaxGrowScale = 1.3;
        private const double SakuraGrowDecayPerTick = 0.85;
        private double _sakuraTreeGrowScale = 1.0;

        // 太空侵略者专属：同一套 BeatGrow，两只小怪物用各自的 ScaleTransform（Invader1/2PokeScale），
        // 但共用同一份数值——鼓点一来两只一起"puff"一下，像游戏里同步被打中的那种反馈感
        private const double InvadersBeatGrowAmount = 2.2;
        private const double InvadersMaxGrowScale = 1.35;
        private const double InvadersGrowDecayPerTick = 0.8;
        private double _invadersBeatGrowScale = 1.0;

        // 雨夜/极光雪夜专属：BeatFlash 落在各自窗景 Border 的发光 Opacity 上，静息值跟 XAML 里
        // 写的默认值（0.4）对齐，避免第一帧从 0 淡入的违和感。雨夜取"远处一道闪电"的意象，
        // 极光取"极光辉光突然亮一下"，都比篝火/樱花那种缩放更"安静"，跟这两套皮肤本身的气质相符
        private const double GlowFlashBeatToBoost = 3.2;
        private const double GlowFlashMaxOpacity = 0.9;
        private const double GlowFlashDecayPerTick = 0.8;
        private double _rainGlowFlashOpacity = 0.4;
        private double _auroraGlowFlashOpacity = 0.4;

        // Minecraft 走路变速：跟 SpeedRatio 是同一份数据，UpdateSteveWalkAnimation（MainWindow.Skins.cs）
        // 用它来缩放换腿节奏，不用另外重新算一遍
        private double _steveWalkSpeedRatio = 1.0;

        // Steve 跳跃 / UFO 跳一下：一次性 BeatBounce，各自独立的武装状态
        private bool _steveJumpArmed = true;
        private bool _ufoHopArmed = true;

        // CRT / 赛博朋克：一次性 BeatShake，各自独立的武装状态
        private bool _crtShakeArmed = true;
        private bool _cyberpunkShakeArmed = true;

        // 复古街机：一次性 BeatWobble（柜子晃一下），都市夜景：一次性 BeatBounce（地铁横向突进一下）
        private bool _arcadeWobbleArmed = true;
        private bool _cityJoltArmed = true;

        // 限定"尊贵皇冠"专属：触发阈值比其它皮肤的 BeatTriggerArmedThreshold（0.02f）更高一截——
        // 故意让这个专属动作比其它皮肤的一次性反馈更难触发，只有鼓点真的很强的时候才会"加冕"，
        // 呼应这套皮肤本身"很难拿到"的定位，不是随便一个小节奏就能看到
        private const float CrownFlareArmedThreshold = 0.035f;
        private const float CrownFlareDisarmThreshold = 0.012f;
        private bool _crownFlareArmed = true;

        private void UpdateMusicReactiveSkin()
        {
            var snapshot = _audioVisualizer.GetSnapshot();

            // SpeedRatio 只对用 isControllable=true 启动过的 Storyboard 有效——这个比率所有参与律动
            // 的皮肤共用同一份算法，具体套不套得上取决于 _musicReactiveStoryboards 里有没有东西
            double levelNormalized = Math.Clamp(snapshot.OverallLevel / LevelReference, 0, 1);
            double ratio = BaseSpeedRatio + levelNormalized * (LevelPeakRatio - BaseSpeedRatio) + snapshot.BeatPulse * BeatSpeedBoost;
            ratio = Math.Clamp(ratio, MinSpeedRatio, MaxSpeedRatio);

            foreach (var storyboard in _musicReactiveStoryboards)
            {
                storyboard.SetSpeedRatio(this, ratio);
            }

            if (_settings.Skin == PlayerSkin.Campfire)
            {
                _campfireBeatGrowScale = UpdateBeatPulse(_campfireBeatGrowScale, 1.0, CampfireMaxGrowScale, snapshot.BeatPulse, CampfireBeatGrowAmount, CampfireGrowDecayPerTick);
                CampfirePokeScale.ScaleX = _campfireBeatGrowScale;
                CampfirePokeScale.ScaleY = _campfireBeatGrowScale;
            }

            if (_settings.Skin == PlayerSkin.Sakura)
            {
                _sakuraTreeGrowScale = UpdateBeatPulse(_sakuraTreeGrowScale, 1.0, SakuraMaxGrowScale, snapshot.BeatPulse, SakuraBeatGrowAmount, SakuraGrowDecayPerTick);
                SakuraTreePokeScale.ScaleX = _sakuraTreeGrowScale;
                SakuraTreePokeScale.ScaleY = _sakuraTreeGrowScale;
            }

            if (_settings.Skin == PlayerSkin.Rain)
            {
                _rainGlowFlashOpacity = UpdateBeatPulse(_rainGlowFlashOpacity, 0.4, GlowFlashMaxOpacity, snapshot.BeatPulse, GlowFlashBeatToBoost, GlowFlashDecayPerTick);
                RainSkinGlow.Opacity = _rainGlowFlashOpacity;
            }

            if (_settings.Skin == PlayerSkin.Aurora)
            {
                _auroraGlowFlashOpacity = UpdateBeatPulse(_auroraGlowFlashOpacity, 0.4, GlowFlashMaxOpacity, snapshot.BeatPulse, GlowFlashBeatToBoost, GlowFlashDecayPerTick);
                AuroraSkinGlow.Opacity = _auroraGlowFlashOpacity;
            }

            if (_settings.Skin == PlayerSkin.Minecraft)
            {
                _steveWalkSpeedRatio = ratio;
                if (TryArmBeatTrigger(ref _steveJumpArmed, snapshot.BeatPulse, BeatTriggerArmedThreshold, BeatTriggerDisarmThreshold))
                {
                    PlaySteveJump();
                }
            }

            if (_settings.Skin == PlayerSkin.Starry)
            {
                if (TryArmBeatTrigger(ref _ufoHopArmed, snapshot.BeatPulse, BeatTriggerArmedThreshold, BeatTriggerDisarmThreshold))
                {
                    PlayUfoHop();
                }
            }

            if (_settings.Skin == PlayerSkin.Crt)
            {
                if (TryArmBeatTrigger(ref _crtShakeArmed, snapshot.BeatPulse, BeatTriggerArmedThreshold, BeatTriggerDisarmThreshold))
                {
                    PlayBeatShake(RetroTvShakeTransform, 3, 180);
                }
            }

            if (_settings.Skin == PlayerSkin.Cyberpunk)
            {
                if (TryArmBeatTrigger(ref _cyberpunkShakeArmed, snapshot.BeatPulse, BeatTriggerArmedThreshold, BeatTriggerDisarmThreshold))
                {
                    PlayBeatShake(HoloRobotShakeTransform, 2.5, 160);
                }
            }

            if (_settings.Skin == PlayerSkin.Invaders)
            {
                _invadersBeatGrowScale = UpdateBeatPulse(_invadersBeatGrowScale, 1.0, InvadersMaxGrowScale, snapshot.BeatPulse, InvadersBeatGrowAmount, InvadersGrowDecayPerTick);
                Invader1PokeScale.ScaleX = _invadersBeatGrowScale;
                Invader1PokeScale.ScaleY = _invadersBeatGrowScale;
                Invader2PokeScale.ScaleX = _invadersBeatGrowScale;
                Invader2PokeScale.ScaleY = _invadersBeatGrowScale;
            }

            if (_settings.Skin == PlayerSkin.Arcade)
            {
                if (TryArmBeatTrigger(ref _arcadeWobbleArmed, snapshot.BeatPulse, BeatTriggerArmedThreshold, BeatTriggerDisarmThreshold))
                {
                    PlayBeatWobble(ArcadeCabinetWobble, 7, 220);
                }
            }

            if (_settings.Skin == PlayerSkin.City)
            {
                if (TryArmBeatTrigger(ref _cityJoltArmed, snapshot.BeatPulse, BeatTriggerArmedThreshold, BeatTriggerDisarmThreshold))
                {
                    PlayBeatBounce(TrainJoltTransform, TranslateTransform.XProperty, 10, 150);
                }
            }

            if (_settings.Skin == PlayerSkin.Crown)
            {
                if (TryArmBeatTrigger(ref _crownFlareArmed, snapshot.BeatPulse, CrownFlareArmedThreshold, CrownFlareDisarmThreshold))
                {
                    PlayCrownFlare();
                }
            }
        }

        // BeatBounce 动画本体：戳一下 Steve（鼠标点击）、鼓点触发（UpdateMusicReactiveSkin）共用同一个
        // 动画。axisProperty 传哪个就弹在哪个方向——Steve/UFO 传 Y（纵向跳），City 的地铁传 X
        // （横向突进一下，见 PlayerSkin.City 那段）。FillBehavior=Stop 很关键：BeginAnimation 默认播完
        // 之后动画本身还会一直占着这个属性（FillBehavior 默认是 HoldEnd），不会自动让出来——设成 Stop
        // 播完就把属性交还，不留一个"看不见但一直占着"的坑（篝火那边刚踩过同一个坑）。
        private static void PlayBeatBounce(TranslateTransform transform, DependencyProperty axisProperty, double amount, double durationMs)
        {
            var jumpAnim = new DoubleAnimation(0, amount, TimeSpan.FromMilliseconds(durationMs))
            {
                AutoReverse = true,
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            transform.BeginAnimation(axisProperty, jumpAnim);
        }

        // Steve 用的是 SteveTransform 的 Y 分量——走路用的是同一个 TranslateTransform 对象的 X 分量
        // （StartSteveWalking 里的 Forever 循环），两个不同属性，互不打架
        private void PlaySteveJump() => PlayBeatBounce(SteveTransform, TranslateTransform.YProperty, -14, 140);

        // UFO 的 X 分量已经被 StartUfoDrift 那个横向飘过的循环占着，Y 分量是空的，跳一下落在 Y 上，
        // 跟 Steve 同一个道理（一个 TranslateTransform 上两个属性各管各的）
        private void PlayUfoHop() => PlayBeatBounce(UfoDriftTransform, TranslateTransform.YProperty, -10, 160);

        // BeatShake 动画本体：一次性左右晃两下再回正，比 BeatBounce 的单次起落更"毛躁"，
        // 用在 CRT/赛博朋克这两套"数字故障感"的皮肤上，跟其余皮肤的"加速"手感区分开
        private static void PlayBeatShake(TranslateTransform transform, double amount, double durationMs)
        {
            var frames = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            frames.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            frames.KeyFrames.Add(new LinearDoubleKeyFrame(-amount, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(durationMs * 0.25))));
            frames.KeyFrames.Add(new LinearDoubleKeyFrame(amount, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(durationMs * 0.5))));
            frames.KeyFrames.Add(new LinearDoubleKeyFrame(-amount * 0.5, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(durationMs * 0.75))));
            frames.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(durationMs))));
            transform.BeginAnimation(TranslateTransform.XProperty, frames);
        }

        // BeatWobble 动画本体：一次性朝一个方向晃过去、再回摆一下、最后回正，落在 RotateTransform.Angle
        // 上——跟 BeatShake 是同一个"上升沿触发一次性反馈"的家族，区别是"转"而不是"平移"。
        // 用在复古街机柜上，像被人撞了一下机身歪一下的感觉，跟 CRT/赛博朋克那种数字故障感的抖动
        // 是完全不同的视觉语言，不会显得是同一招换了个皮肤
        private static void PlayBeatWobble(RotateTransform transform, double amount, double durationMs)
        {
            var frames = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            frames.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            frames.KeyFrames.Add(new LinearDoubleKeyFrame(amount, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(durationMs * 0.3))));
            frames.KeyFrames.Add(new LinearDoubleKeyFrame(-amount * 0.4, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(durationMs * 0.65))));
            frames.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(durationMs))));
            transform.BeginAnimation(RotateTransform.AngleProperty, frames);
        }

        // 限定"尊贵皇冠"专属动作："加冕闪耀"——鼓点够强的时候，皇冠瞬间放大一截 + 辉光冲到最亮 +
        // 背后的光环亮起来快速转半圈。三件事在同一次触发里一起播，不是拿 BeatGrow/BeatFlash/BeatBounce
        // 这些已有方法拼出来的——专门为这套限定皮肤写的一整套动画，别的皮肤不会调用这个方法，
        // 也不共用这几个动画参数，是名副其实的"这个动作别的皮肤用不了"。
        //
        // CrownGlow（ImgCrown 自己的发光）和 CrownHaloRing 的透明度都没有被任何 Storyboard 占着
        // （皮肤本身的呼吸辉光落在 CrownSkinBg 的效果上，是另一个对象），可以直接 BeginAnimation，
        // 跟篝火那套"缩放不碰 CampfireGlow.Opacity 是因为那个被占着"是同一个道理、但这边反过来
        // ——正因为没被占着，才可以直接用。
        //
        // 光环的旋转故意不用 AutoReverse：转过去就停在那，不弹回来，下一次触发从当前角度接着往前转，
        // 是这个方法里唯一一个"不回弹"的动画，跟其它一次性反馈的手感刻意区分开
        private void PlayCrownFlare()
        {
            var scaleAnim = new DoubleAnimation(1.0, 1.5, TimeSpan.FromMilliseconds(260))
            {
                AutoReverse = true,
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            CrownPokeScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            CrownPokeScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);

            var glowAnim = new DoubleAnimation(CrownGlow.Opacity, 1.0, TimeSpan.FromMilliseconds(220))
            {
                AutoReverse = true,
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            CrownGlow.BeginAnimation(DropShadowEffect.OpacityProperty, glowAnim);

            var haloOpacityAnim = new DoubleAnimation(CrownHaloRing.Opacity, 1.0, TimeSpan.FromMilliseconds(180))
            {
                AutoReverse = true,
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            CrownHaloRing.BeginAnimation(UIElement.OpacityProperty, haloOpacityAnim);

            double fromAngle = CrownHaloRotate.Angle;
            var rotateAnim = new DoubleAnimation(fromAngle, fromAngle + 180, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            CrownHaloRotate.BeginAnimation(RotateTransform.AngleProperty, rotateAnim);
        }

        private void ImgSteve_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true; // 别让这次点击被 Window 上挂的 MouseDown 当成拖窗口/双击进 Mini 模式处理
            PlaySteveJump();
        }

        // 戳一下篝火：图标"旺"一下（缩放脉冲），特意不碰 CampfireGlow.Opacity——那个属性已经被
        // CampfireAnimation 那个 Forever 循环占着，插一手会跟它打架；缩放是完全不相关的另一个属性。
        //
        // FillBehavior=Stop 是这里的关键，之前漏了这个：BeginAnimation 默认播完之后动画还会一直
        // "占着"这个属性不撒手（FillBehavior 默认是 HoldEnd），此后 UpdateMusicReactiveSkin 那边想用
        // CampfirePokeScale.ScaleX = ... 直接写值，完全没有效果——这就是"点一次戳一下之后，鼓点变大
        // 那个效果就再也不会动了"的真正原因：不是鼓点检测坏了，是点击动画播完之后一直没把属性交还
        // 回去。设成 Stop 之后，动画播完（120ms 变大 + 120ms 缩回，AutoReverse 自己回到 1.0）就会
        // 主动放手，UpdateMusicReactiveSkin 那边下一个 tick 就能重新接管这个属性。
        private void ImgCampfire_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;

            var scaleX = new DoubleAnimation(1.0, 1.35, TimeSpan.FromMilliseconds(120))
            {
                AutoReverse = true,
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            var scaleY = new DoubleAnimation(1.0, 1.35, TimeSpan.FromMilliseconds(120))
            {
                AutoReverse = true,
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            CampfirePokeScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
            CampfirePokeScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        }
    }
}
