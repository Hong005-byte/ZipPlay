using Android.App;
using Android.Content;
using Android.Media;
using Android.Media.Projection;
using Android.OS;

namespace PixelLyric8Bit.Mobile.Droid;

/// <summary>
/// 皮肤音乐律动的 Android 实现——"整体响度 + 鼓点冲击"实时分析，喂给 FloatingOverlayService 的
/// IconMotionState 当播放速率倍率，对应桌面版 MainWindow.SkinInteractions.cs 的
/// UpdateMusicReactiveSkin（那边数据源是 AudioVisualizer.cs 的 WASAPI 回环采集 + FFT）。
///
/// 这版是简化实现，不做频谱分解：直接对每次从 AudioRecord 读到的 PCM 缓冲区算 RMS（时域响度），
/// 喂给 Core 的 AudioVisualizerMath.CompressMagnitude/ComputeImpact——这两个函数本身跟输入是时域
/// 还是频域无关，压缩/冲击检测这套数学是通用的，只是桌面版拿"某个频段的 FFT 幅度"当 instant、
/// 这边拿"整段 PCM 的 RMS"当 instant，牺牲的是"只看低频判断鼓点"那种精确度（可能把明显的人声/
/// 高频瞬态也当成一次"冲击"），换来的是不用现场写一份 FFT——多数流行乐鼓点本身就是全频段能量的
/// 瞬间跃升，用整体 RMS 抓拍子在实践上够用。CompressMagnitude/ComputeImpact 本身已经在 Core 有
/// 单元测试锁住边界行为，这边不重新测这两个函数，真机验证的是"喂给它时域 RMS 这条链路能不能跑通、
/// 数值量级对不对"。
///
/// 音频源是 AudioPlaybackCaptureConfiguration（API 29+）——这是 Android 官方"抓别的 App 正在播放的
/// 声音"的唯一途径，必须绑定一个 MediaProjection token，而 MediaProjection 必须经过一次系统"开始
/// 录制"同意框才能拿到，见 MainActivity.Android.cs 的 RequestAudioCaptureConsent——这是 Android
/// 系统本身的限制，不是我们自己想加的流程。拿到的 token 不会持久化，也不跟着 App 一起重启：
/// 进程被杀掉重开、或者这个功能被关掉再打开，都得重新走一次这个同意框；Android 14/API 34 起，
/// 用这个 token 采集期间系统还会在状态栏常驻一个"正在被捕获"的提示，这是系统行为，不是本 App
/// 自己加的。
///
/// 只在 Android 10（API 29）以上生效——AudioPlaybackCaptureConfiguration 本身就是这个版本才有的
/// API，低于这个版本 RequestAudioCaptureConsent 直接不发起请求，皮肤律动退回固定节奏，不报错。
/// </summary>
internal static class AudioReactiveCapture
{
    // 响度→速度倍率的几个常量，照抄桌面版 MainWindow.SkinInteractions.cs 的
    // UpdateMusicReactiveSkin/ApplyReactiveSensitivity，让手感尽量对齐——BaseSpeedRatio 是"安静时"
    // 的速率（比正常速度慢，这是桌面版的既有设计，不是这版新加的），MinSpeedRatio/MaxSpeedRatio 是
    // 最终倍率的硬夹紧范围，见 FloatingOverlayService.IconMotionState.Tick 怎么用这几个数（还包括
    // 每份 animation 各自的 sensitivity 换算）
    public const double BaseSpeedRatio = 0.7;
    public const double MinSpeedRatio = 0.4;
    public const double MaxSpeedRatio = 2.6;
    private const double LevelReference = 0.32;
    private const double LevelPeakRatio = 2.0;
    private const double BeatSpeedBoost = 1.2;

    // 冲击检测阈值——桌面版是对着"低频段 FFT 幅度"调的，这边喂的是整段 PCM 的 RMS（时域，没有做
    // 频谱分解，见类顶部注释），量级不一样，这几个数是照着量级换算过的经验值，不是原样照抄，真机
    // 没有反复调过灵敏度，先给一版能跑的，以后觉得"反应太钝/太敏感"随时可以只改这几个数字
    private const float BaselineEma = 0.05f;
    private const float BeatTriggerRatio = 1.5f;
    private const float BeatMinFloor = 0.12f;
    private const double CompressionGain = 6.0;
    private const double CompressionReference = 0.2;

    private const int SampleRate = 44100;
    private const int ReadBufferSamples = 2048;

    private static MediaProjection? _mediaProjection;
    private static MediaProjectionCallback? _projectionCallback;
    private static AudioRecord? _audioRecord;
    private static System.Threading.Thread? _readThread;
    private static volatile bool _stopping;
    private static float _baseline;

    /// <summary>true = 真的在读音频、RawRatio 是有意义的实时数据；false = 没有活跃采集（没授权/
    /// 授权被收回/这一轮进程还没重新授权过），这时任何 animation 都不该被这份数据影响，见
    /// IconMotionState.Tick 里怎么判断这个属性。</summary>
    public static bool IsActive { get; private set; }

    /// <summary>IsActive 从 false 变 true（或者反过来）的时候触发——FloatingOverlayService 订阅这个
    /// 来决定要不要把自己的前台服务类型升级成带 mediaProjection（Android 14+ 要求：只有真的在用这个
    /// token 采集的这段时间才该声明这个类型，见 FloatingOverlayService.RefreshForegroundServiceType）。
    /// 可能从任意线程触发（音频读取线程报错退出、MediaProjection.Callback 的 Handler 线程），订阅方
    /// 自己负责切回主线程再碰 UI/Service API。</summary>
    public static event Action? ActiveChanged;

    /// <summary>还没套 sensitivity 之前的原始速率倍率——所有正在播的 animation 共用同一份（跟桌面版
    /// UpdateMusicReactiveSkin 里那一个 ratio 给所有 Storyboard 共用是同一个道理），各自的 sensitivity
    /// 差异在 IconMotionState 那边才应用，见类顶部注释。</summary>
    public static double RawRatio { get; private set; } = BaseSpeedRatio;

    /// <summary>MainActivity.OnActivityResult 拿到用户同意后调这个——真正建 AudioRecord、起读取线程
    /// 都在这一步。任何一步失败（这套 API 在不同设备/系统版本上的行为一致性没有全面验证过）都会
    /// 静默放弃、清理掉已经建了一半的资源，不崩溃、不弹错误吓用户，皮肤律动退回固定节奏就是了——
    /// 但 catch 里那行 Log.Error 是故意留着的，不是调试完忘了删：这条链路（MediaProjection→
    /// AudioPlaybackCaptureConfiguration→AudioRecord）横跨好几层新 API，真机验证过至少一种失败模式
    /// （Android 14+ 要求拿 token 前就已经是带 TypeMediaProjection 的前台服务在跑，见
    /// FloatingOverlayService.StartForegroundWithNotification 的注释），不排除其它设备/系统版本上
    /// 还有没趟出来的坑，静默失败但完全没有日志痕迹会让以后排查这类问题无从下手。</summary>
    public static void OnConsentGranted(MediaProjectionManager? manager, Result resultCode, Intent data)
    {
        StopInternal(); // 保险起见，先把上一份可能还没清干净的采集停掉，不叠两份同时读

        if (manager == null) return;

        try
        {
            var projection = manager.GetMediaProjection((int)resultCode, data);
            Android.Util.Log.Debug("ZipPlayAudio", $"GetMediaProjection -> {(projection == null ? "null" : "ok")}");
            if (projection == null) return;

            _mediaProjection = projection;
            _projectionCallback = new MediaProjectionCallback(StopInternal);
            projection.RegisterCallback(_projectionCallback, new Handler(Looper.MainLooper!));

            var config = new AudioPlaybackCaptureConfiguration.Builder(projection)
                .AddMatchingUsage(AudioUsageKind.Media)
                .AddMatchingUsage(AudioUsageKind.Game)
                .AddMatchingUsage(AudioUsageKind.Unknown)
                .Build();
            Android.Util.Log.Debug("ZipPlayAudio", $"config built -> {(config == null ? "null" : "ok")}");

            // AudioFormat.Builder.SetChannelMask 吃的是 ChannelOut 这个枚举类型（这套绑定没有区分
            // "录制用的声道掩码"跟"播放用的声道掩码"两种类型，AudioFormat 本身也确实是录制/播放共用
            // 的一个类），下面 AudioRecord.GetMinBufferSize 那边官方签名才是真的要 ChannelIn——两处
            // 传的都是"单声道"，只是 C# 绑定给的类型名不一样，不是传错了方向
            var format = new AudioFormat.Builder()
                !.SetEncoding(Android.Media.Encoding.Pcm16bit)
                !.SetSampleRate(SampleRate)
                !.SetChannelMask(ChannelOut.Mono)
                !.Build();
            Android.Util.Log.Debug("ZipPlayAudio", $"format built -> {(format == null ? "null" : "ok")}");

            int minBufferSize = AudioRecord.GetMinBufferSize(SampleRate, ChannelIn.Mono, Android.Media.Encoding.Pcm16bit);
            Android.Util.Log.Debug("ZipPlayAudio", $"minBufferSize -> {minBufferSize}");
            if (minBufferSize <= 0) minBufferSize = ReadBufferSamples * 2;

            var record = new AudioRecord.Builder()
                !.SetAudioPlaybackCaptureConfig(config)
                !.SetAudioFormat(format)
                !.SetBufferSizeInBytes(minBufferSize * 2)
                !.Build();
            Android.Util.Log.Debug("ZipPlayAudio", $"record built -> {(record == null ? "null" : record.State.ToString())}");

            if (record == null || record.State != Android.Media.State.Initialized)
            {
                record?.Release();
                return;
            }

            _audioRecord = record;
            _baseline = 0f;
            RawRatio = BaseSpeedRatio;
            _stopping = false;

            record.StartRecording();
            Android.Util.Log.Debug("ZipPlayAudio", $"StartRecording -> RecordingState={record.RecordingState}");
            IsActive = true;
            ActiveChanged?.Invoke();

            _readThread = new System.Threading.Thread(ReadLoop) { IsBackground = true, Name = "ZipPlayAudioReactive" };
            _readThread.Start();
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error("ZipPlayAudio", "OnConsentGranted failed: " + ex);
            StopInternal();
        }
    }

    /// <summary>用户在系统同意框上点了拒绝/取消——什么都不用做，IsActive 本来就是 false，皮肤律动
    /// 继续用固定节奏，不弹错误。</summary>
    public static void OnConsentDenied()
    {
    }

    private static void ReadLoop()
    {
        var record = _audioRecord;
        if (record == null) return;

        var buffer = new short[ReadBufferSamples];
        while (!_stopping)
        {
            int read;
            try
            {
                read = record.Read(buffer, 0, buffer.Length);
            }
            catch
            {
                break; // AudioRecord 被外部 Stop/Release 了（比如 StopInternal 正在别的线程跑），正常收尾
            }
            if (read <= 0) continue;

            double sumSquares = 0;
            for (int i = 0; i < read; i++)
            {
                double sample = buffer[i] / 32768.0;
                sumSquares += sample * sample;
            }
            double rms = Math.Sqrt(sumSquares / read);

            float level = PixelLyric8BitFix.AudioVisualizerMath.CompressMagnitude(rms, CompressionGain, CompressionReference);
            float impact = PixelLyric8BitFix.AudioVisualizerMath.ComputeImpact(level, ref _baseline, BaselineEma, BeatTriggerRatio, BeatMinFloor);

            double levelNormalized = Math.Clamp(level / LevelReference, 0, 1);
            double ratio = BaseSpeedRatio + levelNormalized * (LevelPeakRatio - BaseSpeedRatio) + impact * BeatSpeedBoost;
            RawRatio = Math.Clamp(ratio, MinSpeedRatio, MaxSpeedRatio);
        }
    }

    /// <summary>关掉皮肤音乐律动这个开关、悬浮窗被关闭、Service 整个销毁——都会调到这里，把
    /// AudioRecord/MediaProjection 这些系统资源老老实实还回去，不留着白占（尤其 MediaProjection
    /// 不主动 Stop 的话，Android 15 那个"正在被捕获"的状态栏提示会一直挂着，很显眼）。</summary>
    public static void Stop() => StopInternal();

    private static void StopInternal()
    {
        bool wasActive = IsActive;
        _stopping = true;
        IsActive = false;
        RawRatio = BaseSpeedRatio;
        if (wasActive) ActiveChanged?.Invoke();

        try { _readThread?.Join(500); } catch { }
        _readThread = null;

        try { _audioRecord?.Stop(); } catch { }
        try { _audioRecord?.Release(); } catch { }
        _audioRecord = null;

        if (_mediaProjection != null && _projectionCallback != null)
        {
            try { _mediaProjection.UnregisterCallback(_projectionCallback); } catch { }
        }
        try { _mediaProjection?.Stop(); } catch { }
        _mediaProjection = null;
        _projectionCallback = null;
    }

    /// <summary>MediaProjection.Callback 是抽象类不是接口，得真的继承——只关心 OnStop（用户从系统
    /// 状态栏那个"正在被捕获"提示里主动点了停止，或者系统出于别的原因收回了这个 token），转发给
    /// StopInternal 把我们这边的资源也一起清掉，不然 AudioRecord 会对着一个已经失效的 token 空转。</summary>
    private sealed class MediaProjectionCallback : MediaProjection.Callback
    {
        private readonly Action _onStop;
        public MediaProjectionCallback(Action onStop) => _onStop = onStop;
        public override void OnStop() => _onStop();
    }
}
