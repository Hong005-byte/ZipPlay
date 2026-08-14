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
    // 抓词 / 解析 / 显示的整条链路：切歌触发抓词 -> 本地缓存优先 -> 四引擎并发兜底 -> 解析 LRC -> 按播放位置渲染。
    // 也包含歌词同步偏移、卡拉OK扫光效果、双语歌词这几个跟"这一行歌词具体怎么显示"相关的开关。
    public partial class MainWindow : Window
    {
        // 歌词框上滚一下滚轮：每格 ±50ms，矫正播放源上报位置跟实际听感之间的固定延迟。
        // 立刻存盘（不等关窗口），这样调好之后哪怕软件崩了/强制关了也不会白调
        private void LyricBox_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            AdjustLyricOffset(e.Delta > 0 ? 50 : -50, replace: false);
        }

        private void AdjustLyricOffset(int deltaOrValue, bool replace)
        {
            _lyricOffsetMs = replace ? deltaOrValue : Math.Clamp(_lyricOffsetMs + deltaOrValue, -5000, 5000);
            _settings.LyricOffsetMs = _lyricOffsetMs;
            _settings.Save();

            string sign = _lyricOffsetMs >= 0 ? "+" : "";
            ShowToast($"🔄 歌词偏移 {sign}{_lyricOffsetMs}ms");
        }

        // 卡拉OK扫光开关：关了就退回最早那种"整行一次性切换"的显示方式，跟加这个效果之前完全一样
        private void ToggleKaraokeEffect()
        {
            _settings.KaraokeEffectEnabled = !_settings.KaraokeEffectEnabled;
            _settings.Save();
            UpdateKaraokeToggleIcon();
            ShowToast(_settings.KaraokeEffectEnabled ? "🎤 卡拉OK效果已开启" : "🎤 卡拉OK效果已关闭");
            _lastKaraokeLineIndex = -1; // 强制下一 tick 按新的开关状态重新渲染当前这句
        }

        // 白色=开启，黑色=关闭——矢量图标（不是 emoji）才能这样直接换实心颜色，一眼看出状态
        private void UpdateKaraokeToggleIcon()
        {
            var brush = _settings.KaraokeEffectEnabled ? Brushes.White : Brushes.Black;
            KaraokeIconHead.Background = brush;
            KaraokeIconStand.Stroke = brush;
            KaraokeIconPole.Stroke = brush;
        }

        private void BtnKaraokeToggle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ToggleKaraokeEffect();
        }

        // 双语歌词开关：关了不代表这首歌就没翻译了，只是先不显示；重新开回来，如果当前歌词本来就有
        // 翻译数据（这次是网易云抓到的），下一 tick 立刻就能看见，不用切歌重新抓一遍
        private void ToggleBilingualLyrics()
        {
            _settings.BilingualLyricsEnabled = !_settings.BilingualLyricsEnabled;
            _settings.Save();
            UpdateBilingualToggleIcon();
            ShowToast(_settings.BilingualLyricsEnabled ? "🌐 双语歌词已开启" : "🌐 双语歌词已关闭");
            if (!_settings.BilingualLyricsEnabled) TxtTranslationLyric.Visibility = Visibility.Collapsed;
        }

        // 双语开关是 emoji 图标，Foreground 对 emoji 不起作用（Windows 上 emoji 是自带颜色的彩色字形），
        // 只能靠 Opacity 区分开/关，跟改用矢量图标之前的卡拉OK开关是同一个道理
        private void UpdateBilingualToggleIcon()
        {
            TxtBilingualIcon.Opacity = _settings.BilingualLyricsEnabled ? 1.0 : 0.5;
        }

        private void BtnBilingualToggle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ToggleBilingualLyrics();
        }

        private async Task HandleTrackChangeAsync(string title, string artist)
        {
            if (string.IsNullOrEmpty(title)) return;
            string trackId = $"{title}_{artist}";
            if (_lastTrackId == trackId) return;
            _lastTrackId = trackId;

            // 1. 切歌一瞬间，UI 立刻响应，绝不等待网络
            Dispatcher.Invoke(() =>
            {
                TxtSongTitle.Text = title.ToUpper();
                TxtArtist.Text = $"BY {artist.ToUpper()}";
                TxtDynamicLyric.Text = "⛏️ MINING NEW TRACK...";
                TxtTranslationLyric.Visibility = Visibility.Collapsed;
                TxtTime.Text = "[00:00 / 00:00]";
                LyricProgressBar.Value = 0;
            });
            lock (_lyricLock)
            {
                _lyricLines = new List<(int, string)>();
                _translationLines = new List<(int, string)>();
                _lyricCursor = -1;
            }
            _lastKaraokeLineIndex = -1;

            // 2. 取消上一首还没返回的抓词请求，防止旧结果覆盖新歌，也少占带宽
            _lyricFetchCts?.Cancel();
            var cts = new CancellationTokenSource();
            _lyricFetchCts = cts;

            // 3. 立刻按新 session 状态刷新一次锚点（避免残留上一首的进度），
            //    顺便记下这首歌"官方"的真实时长——要放在抓词之前，因为抓词回来还要拿它校验版本对不对
            if (_currentSession != null) RefreshAnchor(_currentSession);
            TimeSpan expectedDuration = _totalDuration;

            // 4. 歌名数据清洗
            string cleanTitle = Regex.Replace(title, @"\s*[\(\[][^\]\)]*(feat|with|remix|version|prod)[^\]\)]*[\)\]]", "", RegexOptions.IgnoreCase).Trim();
            string cleanArtist = artist.Split(new[] { ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? artist;

            // 4.5 先查本地缓存：这首歌以前成功抓到过的话直接用，完全不用等网络。
            //     缓存文件只存了原文那一份 LRC，没有翻译（翻译只在这次是新抓的、且正好是网易云赢了并发的时候才有），
            //     所以缓存命中这条路走不出双语——这是个有意的取舍，不为了翻译放弃"缓存秒出"这个更重要的体验。
            string? cachedLrc = LyricsCache.TryGet(trackId);
            if (!string.IsNullOrEmpty(cachedLrc) && LyricsFetcher.IsDurationPlausible(cachedLrc, expectedDuration))
            {
                ParseLrcText(cachedLrc);
                return;
            }

            // 6. 核心：四个免费歌词源并发抓取，谁先给出有效结果就用谁，主线程完全不等待
            _ = Task.Run(() => FetchLyricsAnyEngineAsync(cleanTitle, cleanArtist, trackId, expectedDuration, cts.Token));
        }

        // 四引擎并发抓词的具体实现全搬到 LyricsFetcher 里了，这里只负责：拿结果 -> 存缓存 -> 解析显示，
        // 或者拿不到结果就报个"没找到"
        private async Task FetchLyricsAnyEngineAsync(string title, string artist, string trackId, TimeSpan expectedDuration, CancellationToken token)
        {
            var result = await _lyricsFetcher.FetchAsync(title, artist, expectedDuration, token);

            if (token.IsCancellationRequested || trackId != _lastTrackId) return; // 已经切歌了，这个结果作废

            if (result == null)
            {
                Dispatcher.Invoke(() =>
                {
                    lock (_lyricLock)
                    {
                        if (_lyricLines.Count == 0) TxtDynamicLyric.Text = "NO LYRICS FOUND (4 ENGINES TRIED)";
                    }
                });
                return;
            }

            LyricsCache.Save(trackId, result.Lrc); // 只缓存原文，见 HandleTrackChangeAsync 里的说明
            ParseLrcText(result.Lrc);
            SetTranslation(result.TranslationLrc);
        }

        // 四个引擎都找不到时（小众歌/纯音乐之类）的兜底：手动选一个本地 .lrc 文件用上。
        // 用户自己选的，比自动抓的更可信，不走 IsDurationPlausible 那套版本校验；
        // 存进跟自动抓词共用的那份缓存，下次再放这首歌直接命中，不用重新选一遍。
        private void ImportLocalLrc()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "LRC 歌词文件 (*.lrc)|*.lrc|所有文件 (*.*)|*.*",
                Title = "选择这首歌对应的歌词文件",
            };

            if (dialog.ShowDialog(this) != true) return;

            try
            {
                string content = File.ReadAllText(dialog.FileName);
                if (string.IsNullOrWhiteSpace(content))
                {
                    TxtDynamicLyric.Text = "⚠️ 文件是空的";
                    return;
                }

                _lyricFetchCts?.Cancel(); // 别让还没返回的自动抓词结果晚一步把手动导入的覆盖掉

                if (!string.IsNullOrEmpty(_lastTrackId)) LyricsCache.Save(_lastTrackId, content);
                ParseLrcText(content);
                SetTranslation(null); // 手动导入的文件不带翻译，清掉可能残留的上一首翻译
            }
            catch (Exception ex)
            {
                TxtDynamicLyric.Text = "⚠️ 歌词文件读取失败";
                AppLog.Error("ImportLocalLrc", ex);
            }
        }

        private void ParseLrcText(string lrcContent)
        {
            var sorted = LrcParser.ParseLines(lrcContent);
            if (sorted.Count == 0) return;

            lock (_lyricLock)
            {
                _lyricLines = sorted;
                _lyricCursor = -1;
            }
        }

        private void SetTranslation(string? translationLrcContent)
        {
            var sorted = LrcParser.ParseLines(translationLrcContent);
            lock (_lyricLock)
            {
                _translationLines = sorted;
            }
        }

        // 翻译行数量通常就是几十句，按当前播放位置从头找到最后一个"已经到时间"的那句就够了，
        // 不需要像主歌词游标那样做 O(1) 优化——这个查找只在双语开关开着时才会跑，且不是每帧都要重排版
        private string? FindActiveTranslation(int currentMs)
        {
            string? text = null;
            foreach (var (timeMs, t) in _translationLines)
            {
                if (timeMs > currentMs) break;
                text = t;
            }
            return text;
        }

        // 有序列表 + 游标推进：播放中每次都是 O(1)，只有发生倒退 seek 时才重新定位
        private void UpdateLyricDisplay(int currentMs)
        {
            lock (_lyricLock)
            {
                if (_lyricLines.Count == 0)
                {
                    if (!TxtDynamicLyric.Text.Contains("MINING") &&
                        !TxtDynamicLyric.Text.Contains("CRAFTING") &&
                        !TxtDynamicLyric.Text.Contains("NO LYRICS"))
                    {
                        TxtDynamicLyric.Text = "⛏️ MINING SYNCED LYRICS...";
                    }
                    _lastKaraokeLineIndex = -1;
                    TxtTranslationLyric.Visibility = Visibility.Collapsed;
                    return;
                }

                // 时间往回跳了（用户拖动了进度条/倒退），游标失效，重新定位
                if (_lyricCursor >= 0 && _lyricLines[_lyricCursor].TimeMs > currentMs)
                {
                    _lyricCursor = -1;
                }

                if (_lyricCursor < 0 && _lyricLines[0].TimeMs > currentMs)
                {
                    return; // 还没到第一句歌词的时间
                }

                int i = _lyricCursor < 0 ? 0 : _lyricCursor;
                while (i + 1 < _lyricLines.Count && _lyricLines[i + 1].TimeMs <= currentMs) i++;
                _lyricCursor = i;
                string lineText = _lyricLines[i].Text.ToUpper();

                // 双语歌词：只有开关开着、且这首歌真的抓到翻译（目前只有网易云引擎会给）才显示，
                // 跟主歌词是不是走卡拉OK扫光无关，两个开关互不影响
                if (_settings.BilingualLyricsEnabled)
                {
                    string? translation = FindActiveTranslation(currentMs);
                    if (!string.IsNullOrEmpty(translation))
                    {
                        TxtTranslationLyric.Text = translation;
                        TxtTranslationLyric.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        TxtTranslationLyric.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    TxtTranslationLyric.Visibility = Visibility.Collapsed;
                }

                // 开关关着：退回最早那种"整行一次性切换"，跟加卡拉OK效果之前完全一样，不算 sungChars 那些
                if (!_settings.KaraokeEffectEnabled)
                {
                    if (i != _lastKaraokeLineIndex)
                    {
                        _lastKaraokeLineIndex = i;
                        TxtDynamicLyric.Inlines.Clear();
                        TxtDynamicLyric.Text = lineText;
                    }
                    return;
                }

                // 卡拉OK扫光效果：具体的"唱到第几个字"估算逻辑搬到 KaraokeTiming 里了（带经验参数，
                // 独立成类方便单独写单元测试钉住行为），这里只管拿结果去渲染
                int lineStartMs = _lyricLines[i].TimeMs;
                int lineEndMs = (i + 1 < _lyricLines.Count) ? _lyricLines[i + 1].TimeMs : lineStartMs + 4000;
                int sungChars = KaraokeTiming.EstimateSungChars(lineText, currentMs, lineStartMs, lineEndMs);

                // 同一句歌词里，只有"唱到第几个字"这个分割点真的变了才重建 Inlines，
                // 50ms 一 tick 但大多数 tick 分割点不变，不用每次都重新排版
                if (i != _lastKaraokeLineIndex || sungChars != _lastKaraokeSungChars)
                {
                    _lastKaraokeLineIndex = i;
                    _lastKaraokeSungChars = sungChars;
                    RenderKaraokeLine(lineText, sungChars);
                }
            }
        }

        // 用两段不同颜色的 Run 拼出"已经唱到的部分高亮，还没唱到的部分暗淡"的效果。
        // 用 Inlines 而不是裁剪矩形，是因为歌词框允许自动换行（TextWrapping="Wrap"），
        // 换行后裁剪矩形没法正确表达"先填满第一行，再开始填第二行"，Run 拼接则完全不受换行影响。
        private void RenderKaraokeLine(string text, int sungChars)
        {
            TxtDynamicLyric.Inlines.Clear();
            if (sungChars > 0)
            {
                TxtDynamicLyric.Inlines.Add(new Run(text.Substring(0, sungChars)) { Foreground = _karaokeSungBrush });
            }
            if (sungChars < text.Length)
            {
                TxtDynamicLyric.Inlines.Add(new Run(text.Substring(sungChars)) { Foreground = _karaokeUnsungBrush });
            }
        }
    }
}
