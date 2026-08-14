using System;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace PixelLyric8BitFix
{
    /// <summary>整体响度 + 节奏冲击，UI 线程每帧读一份快照用来驱动 Mini 模式那两圈像素粒子。</summary>
    internal readonly struct AudioVisualizerSnapshot
    {
        /// <summary>0~1，所有频段的平滑响度平均值，代表"现在是不是正常在出声"——驱动内圈，音乐正常播放时会持续有值。</summary>
        public float OverallLevel { get; init; }

        /// <summary>0~1，只有低音鼓点或者高音突然变响（相对最近一段时间的平均水平）才会跳起来，其余时间是 0——驱动外圈。</summary>
        public float BeatPulse { get; init; }
    }

    /// <summary>
    /// Mini 模式那两圈像素粒子的数据来源：用 WASAPI 回环采集（<see cref="WasapiLoopbackCapture"/>）
    /// 抓"当前系统正在往扬声器送的那份混音"，跟播放源是谁（Spotify/网易云/浏览器）完全无关，
    /// 不需要那个播放源配合，也不需要额外权限/UAC。
    ///
    /// 这跟歌词同步用的 SMTC 是两套完全独立的数据源：SMTC 只给"标题/播放位置"这些元数据，
    /// 拿不到真实音频；这里反过来，只要声音、不要元数据。
    ///
    /// 早期版本是把 16 个频段各自的能量分别喂给不同的粒子，结果看起来"到处乱跳"——音乐大部分时间
    /// 是宽频带的，很多频段同时有能量，一堆互不相关的点各跳各的，看不出个所以然。现在改成只往外
    /// 暴露两个聚合出来的数字：整体响度（内圈用）、节奏冲击（外圈用），视觉上才会是"平时跟着整体
    /// 响度一起呼吸、鼓点/高音一来才多一圈"这种有意义的节奏感，而不是一堆随机噪点。
    /// </summary>
    internal sealed class AudioVisualizer : IDisposable
    {
        private const int BandCount = 16; // 内部按对数频率切分的频段数，只用来算聚合值，不再对外暴露到每个频段

        private const int FftLength = 1024;   // 2^10
        private const int FftM = 10;          // log2(FftLength)，NAudio 的 FFT 要这个而不是长度本身
        private const float DecayPerFrame = 0.82f; // 经验值：FFT 帧率取决于采样率（一般 44.1k/48k），大约每 20ms 左右一帧
        private const float BeatDecayPerFrame = 0.88f; // 冲击本身的衰减，比每个频段的衰减稍微慢一点，让外圈那一下停留久一点看得清
        private const float BaselineEma = 0.03f;   // 滑动平均的更新速率，比逐帧的 attack/decay 慢很多，代表"最近这段时间大概的响度水平"

        // 压缩幅度用的放大倍数/参考满量程——这两个是"FFT 原始幅度怎么映射到 0~1"，
        // 跟"灵敏度"（下面 ApplySensitivity 调的那三个）是两回事，不受灵敏度设置影响
        private const double CompressionGain = 40;
        private const double CompressionReference = 12.0;

        // 底鼓/贝斯这种"beat"感最强的落在大概 60~150Hz，对应对数切分后的前几个频段
        private const int BassBandStart = 0, BassBandEnd = 4;
        // 镲片/齿音这类"尖"的高音落在大概 2.3k~8kHz，对应对数切分后的最后几个频段
        private const int TrebleBandStart = 12, TrebleBandEnd = 16;

        // 这三个是设置页"律动灵敏度"实际调的参数，默认给中档；见 ApplySensitivity。
        // 16 个频段各自的能量平均下来（内圈用的"整体响度"）天然会比单个频段的读数小很多——
        // 音乐同一瞬间往往只有几个频段是真的响的，平均下来会被大量"没什么动静"的频段拉低，
        // 所以 _overallLevelGain 要把这个平均值乘回来，不然内圈几乎永远到不了能显示出来的门槛。
        private float _overallLevelGain = 3.2f;
        private float _beatTriggerRatio = 1.3f;  // 瞬时能量超过滑动平均这个倍数，才算一次"冲击"（鼓点/高音重音）
        private float _beatMinFloor = 0.06f;     // 瞬时能量太小（安静段落的正常波动）不该触发，得先过这个绝对门槛

        private readonly object _levelLock = new();
        private readonly float[] _smoothed = new float[BandCount];
        private readonly int[] _bandBinStart = new int[BandCount + 1];
        private readonly Complex[] _fftBuffer = new Complex[FftLength];

        private float _overallLevel;
        private float _beatPulse;
        private float _bassBaseline;
        private float _trebleBaseline;

        private WasapiLoopbackCapture? _capture;
        private int _fftPos;
        private bool _isRunning;
        private bool _disabledDueToError; // 出过一次错（比如没有默认输出设备）就不再重试，别反复往日志里写同一个异常

        /// <summary>
        /// 设置页"律动灵敏度"选出来的档位映射到这三个参数——低档更难触发、高档更容易触发，
        /// 每个人电脑的系统音量、常听的音乐类型都不一样，写死一套参数不可能对所有人都刚好合适。
        /// 只影响下一次 <see cref="Start"/> 之后算出来的值，运行中途调用也安全（下一帧就生效）。
        /// </summary>
        public void ApplySensitivity(VisualizerSensitivity sensitivity)
        {
            (_overallLevelGain, _beatTriggerRatio, _beatMinFloor) = sensitivity switch
            {
                VisualizerSensitivity.Low => (2.0f, 1.6f, 0.10f),
                VisualizerSensitivity.High => (4.4f, 1.15f, 0.03f),
                _ => (3.2f, 1.3f, 0.06f), // Medium
            };
        }

        public void Start()
        {
            if (_isRunning || _disabledDueToError) return;

            try
            {
                var capture = new WasapiLoopbackCapture();
                PrecomputeBandRanges(capture.WaveFormat.SampleRate);
                capture.DataAvailable += OnDataAvailable;
                capture.RecordingStopped += OnRecordingStopped;
                capture.StartRecording();

                _capture = capture;
                _isRunning = true;
            }
            catch (Exception ex)
            {
                // 抓不到系统音频（没有默认播放设备、被安全策略挡掉之类）不该拖累 Mini 模式本身，
                // 静默禁用就好——粒子会一直是熄灭状态，其它一切照常
                _disabledDueToError = true;
                AppLog.Error("AudioVisualizer.Start", ex);
                Cleanup();
            }
        }

        public void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;
            Cleanup();

            lock (_levelLock)
            {
                _overallLevel = 0;
                _beatPulse = 0;
            }
            Array.Clear(_smoothed);
            _bassBaseline = 0;
            _trebleBaseline = 0;
            _fftPos = 0;
        }

        /// <summary>UI 线程每帧调用，拿一份最新的整体响度 + 节奏冲击快照，不会跟采集线程打架。</summary>
        public AudioVisualizerSnapshot GetSnapshot()
        {
            lock (_levelLock)
            {
                return new AudioVisualizerSnapshot { OverallLevel = _overallLevel, BeatPulse = _beatPulse };
            }
        }

        public void Dispose() => Cleanup();

        private void Cleanup()
        {
            if (_capture == null) return;
            try { _capture.StopRecording(); } catch { /* 已经在停了/停不掉都无所谓，反正马上 Dispose */ }
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.Dispose();
            _capture = null;
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                _disabledDueToError = true;
                AppLog.Error("AudioVisualizer.RecordingStopped", e.Exception);
            }
        }

        // 只关心跟音乐能量最相关的 60Hz ~ 8kHz 这一段：更低是隆隆的直流/环境底噪，更高基本没有可视化意义。
        // 按对数（不是线性）切分频段——人耳对频率的感知本来就是对数的，线性切分会导致低音那几个频段
        // 挤在一起看不出变化，高音那几个频段又空空荡荡没什么能量
        private void PrecomputeBandRanges(int sampleRate)
        {
            double minFreq = 60;
            double maxFreq = Math.Min(8000, sampleRate / 2.0);
            int usableBins = FftLength / 2;

            for (int i = 0; i <= BandCount; i++)
            {
                double freq = minFreq * Math.Pow(maxFreq / minFreq, (double)i / BandCount);
                int bin = (int)(freq * FftLength / sampleRate);
                _bandBinStart[i] = Math.Clamp(bin, 1, usableBins - 1); // 跳过 bin 0（直流分量，没有可视化意义）
            }
        }

        // 这个回调在 NAudio 自己开的采集线程上跑，不是 UI 线程；只往 _fftBuffer 这个专属缓冲区里写，
        // 攒够 FftLength 个采样就做一次 FFT，跟 UI 线程之间完全靠 GetSnapshot() 的锁交接，没有别的共享状态
        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            var format = _capture?.WaveFormat;
            if (format == null) return;

            int bytesPerSample = format.BitsPerSample / 8;
            int channels = Math.Max(1, format.Channels);
            int frameSize = bytesPerSample * channels;
            if (frameSize <= 0 || bytesPerSample <= 0) return;

            int sampleCount = e.BytesRecorded / frameSize;
            bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat;

            for (int i = 0; i < sampleCount; i++)
            {
                int offset = i * frameSize; // 只取第一个声道（左声道），聚合出来的响度/冲击不需要立体声分离
                float sample = isFloat
                    ? BitConverter.ToSingle(e.Buffer, offset)
                    : BitConverter.ToInt16(e.Buffer, offset) / 32768f;

                // 加一个汉明窗再做 FFT：直接对"裁出来的一段"做 FFT 会因为边界不连续产生频谱泄漏，
                // 窗函数把两端压低到接近 0，频谱看起来干净很多，这是做音频 FFT 的标准操作
                _fftBuffer[_fftPos].X = (float)(sample * FastFourierTransform.HammingWindow(_fftPos, FftLength));
                _fftBuffer[_fftPos].Y = 0;
                _fftPos++;

                if (_fftPos >= FftLength)
                {
                    ProcessFft();
                    _fftPos = 0;
                }
            }
        }

        private void ProcessFft()
        {
            FastFourierTransform.FFT(true, FftM, _fftBuffer);

            Span<float> newLevels = stackalloc float[BandCount];
            for (int b = 0; b < BandCount; b++)
            {
                int start = _bandBinStart[b];
                int end = Math.Max(start + 1, _bandBinStart[b + 1]);

                double sum = 0;
                for (int bin = start; bin < end; bin++)
                {
                    var c = _fftBuffer[bin];
                    sum += Math.Sqrt(c.X * c.X + c.Y * c.Y);
                }
                double avgMagnitude = sum / (end - start);
                newLevels[b] = AudioVisualizerMath.CompressMagnitude(avgMagnitude, CompressionGain, CompressionReference);
            }

            // VU 表那种手法：往上瞬间跳到新值（快 attack），往下按 DecayPerFrame 缓慢衰减（慢 decay）。
            // 纯用瞬时值会抖得很厉害看着毛躁，纯用平滑值又会显得鼓点都"跟不上"，这个组合两头都要。
            // 内圈（整体响度）就是这份平滑值的平均，代表"最近这一刻大概有多响"，波动比单个频段柔和很多。
            float overall = 0;
            for (int b = 0; b < BandCount; b++)
            {
                _smoothed[b] = newLevels[b] > _smoothed[b] ? newLevels[b] : _smoothed[b] * DecayPerFrame;
                overall += _smoothed[b];
            }
            overall = Math.Clamp(overall / BandCount * _overallLevelGain, 0, 1);

            // 外圈（节奏冲击）：低音跟高音各自算一次"是不是比最近的平均水平明显冲高了一截"，
            // 两个里取更明显的那个——鼓点在低频冲、镲片/高音在高频冲，两种都该点亮外圈
            float bassInstant = AudioVisualizerMath.AverageRange(newLevels, BassBandStart, BassBandEnd);
            float trebleInstant = AudioVisualizerMath.AverageRange(newLevels, TrebleBandStart, TrebleBandEnd);
            float bassImpact = AudioVisualizerMath.ComputeImpact(bassInstant, ref _bassBaseline, BaselineEma, _beatTriggerRatio, _beatMinFloor);
            float trebleImpact = AudioVisualizerMath.ComputeImpact(trebleInstant, ref _trebleBaseline, BaselineEma, _beatTriggerRatio, _beatMinFloor);
            float beatTarget = Math.Max(bassImpact, trebleImpact);

            float beatPulse = beatTarget > _beatPulse ? beatTarget : _beatPulse * BeatDecayPerFrame;

            // _bassBaseline/_trebleBaseline 只在这个线程（采集线程）读写，不需要加锁；
            // _overallLevel 和 _beatPulse 会被 UI 线程通过 GetSnapshot() 并发读取，一起塞进锁里赋值，
            // 保证快照里这两个数是同一时刻的，不会一个是这一帧的、一个是上一帧的
            lock (_levelLock)
            {
                _overallLevel = overall;
                _beatPulse = beatPulse;
            }
        }

    }
}
