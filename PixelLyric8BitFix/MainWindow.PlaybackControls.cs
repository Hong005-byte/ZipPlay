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
    // 播放控制：上一首 / 播放-暂停 / 下一首，竖排在窗口右侧。跟歌词同步用的是同一个 _currentSession
    // （SMTC），只是这边调用的是"写"的那几个方法（TrySkipNextAsync 这些），不是"读"的那几个。
    //
    // 不是每个播放源都实现了全部命令——比如不少浏览器标签页只支持暂停、不支持跳歌，
    // 这种情况下 Try*Async 不会抛异常，只会返回 false，按钮该点还是能点，只是点了没反应，
    // 不强求每个播放源都支持全部功能，具体已知的支持情况写在 README 里。
    public partial class MainWindow : Window
    {
        private async void BtnPrevTrack_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (_currentSession == null) return;
            try
            {
                bool ok = await _currentSession.TrySkipPreviousAsync();
                if (!ok) AppLog.Info("PlaybackControl: 当前播放源拒绝了「上一首」命令（可能不支持）");
            }
            catch (Exception ex)
            {
                AppLog.Error("BtnPrevTrack_MouseLeftButtonDown", ex);
            }
        }

        private async void BtnPlayPause_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (_currentSession == null) return;
            try
            {
                bool ok = await _currentSession.TryTogglePlayPauseAsync();
                if (!ok) AppLog.Info("PlaybackControl: 当前播放源拒绝了「播放/暂停」命令（可能不支持）");
            }
            catch (Exception ex)
            {
                AppLog.Error("BtnPlayPause_MouseLeftButtonDown", ex);
            }
        }

        private async void BtnNextTrack_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (_currentSession == null) return;
            try
            {
                bool ok = await _currentSession.TrySkipNextAsync();
                if (!ok) AppLog.Info("PlaybackControl: 当前播放源拒绝了「下一首」命令（可能不支持）");
            }
            catch (Exception ex)
            {
                AppLog.Error("BtnNextTrack_MouseLeftButtonDown", ex);
            }
        }

        // 播放/暂停按钮的图标跟真实播放状态走：正在播就显示暂停图标（两条竖杠），暂停中就显示播放三角形。
        // _isPlaying 本来就在 RefreshAnchor 里跟着系统广播更新，这里只是顺手把图标也带上。
        private void UpdatePlayPauseIcon()
        {
            PlayIcon.Visibility = _isPlaying ? Visibility.Collapsed : Visibility.Visible;
            PauseIconBar1.Visibility = _isPlaying ? Visibility.Visible : Visibility.Collapsed;
            PauseIconBar2.Visibility = _isPlaying ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── 进度条拖拽跳转：点哪跳到哪、拖着放手也跳，图标本体是当前皮肤的小图标（ApplySkinPalette 里设的）。
        // 鼠标事件挂在 ProgressRow 这个 Grid 本身，点击区域是整条进度条，不用精确点在小图标上。──

        // 没有真实时长（还没拿到播放源上报的 duration，比如刚切歌那一瞬间）就不让拖，
        // 拖一个不存在的时长没有意义，松手了也算不出该跳到哪
        private void ProgressRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (_totalDuration <= TimeSpan.Zero) return;

            _isDraggingSeek = true;
            ProgressRow.CaptureMouse();
            UpdateSeekPreview(e.GetPosition(ProgressRow).X);
        }

        private void ProgressRow_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDraggingSeek) return;
            UpdateSeekPreview(e.GetPosition(ProgressRow).X);
        }

        private async void ProgressRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDraggingSeek) return;
            _isDraggingSeek = false;
            ProgressRow.ReleaseMouseCapture();

            double ratio = Math.Clamp(e.GetPosition(ProgressRow).X / Math.Max(1, ProgressRow.ActualWidth), 0, 1);
            var newPosition = TimeSpan.FromMilliseconds(_totalDuration.TotalMilliseconds * ratio);

            // 先在本地把锚点直接拨到新位置——不等播放源确认。大部分播放源要几十到几百毫秒才会
            // 广播新的播放位置，这段等待期间如果不先本地跳过去，进度条/歌词会显得"跳过去又弹回来"，
            // 松手瞬间的观感会很差；真跳错了的话，播放源随后广播的真实状态也会自然纠正回来。
            _anchorPosition = newPosition;
            _anchorTime = DateTimeOffset.Now;

            if (_currentSession != null)
            {
                try
                {
                    bool ok = await _currentSession.TryChangePlaybackPositionAsync(newPosition.Ticks);
                    if (!ok) AppLog.Info("PlaybackControl: 当前播放源拒绝了「跳转播放位置」命令（可能不支持）");
                }
                catch (Exception ex)
                {
                    AppLog.Error("ProgressRow_MouseLeftButtonUp (seek)", ex);
                }
            }
        }

        // 拖动/点击过程中实时挪动图标位置 + 同步进度条填充，让用户立刻看到"松手就会跳到这"，
        // 不用等真的松手才有反馈
        private void UpdateSeekPreview(double mouseX)
        {
            double trackWidth = ProgressRow.ActualWidth;
            if (trackWidth <= 0) return;

            double ratio = Math.Clamp(mouseX / trackWidth, 0, 1);
            LyricProgressBar.Value = LyricProgressBar.Maximum * ratio;
            PositionSeekThumb(ratio, trackWidth);
        }

        // 非拖动状态下，每 tick 跟着真实播放进度挪动图标；拖动状态下由 UpdateSeekPreview 直接控制，
        // 这里不要跟它打架（SmoothTimer_Tick 里已经用 _isDraggingSeek 挡住了，不会同时调用两边）
        private void UpdateSeekThumbPosition()
        {
            double trackWidth = ProgressRow.ActualWidth;

            // 还没拿到真实时长（刚切歌、或者这个播放源压根不上报时长）时，进度是个没意义的假值，
            // 图标藏起来，别让用户以为能拖一个其实拖不动/拖了也没用的东西
            if (trackWidth <= 0 || _totalDuration <= TimeSpan.Zero)
            {
                SeekThumbIcon.Visibility = Visibility.Collapsed;
                return;
            }

            SeekThumbIcon.Visibility = Visibility.Visible;
            double ratio = LyricProgressBar.Maximum > 0 ? LyricProgressBar.Value / LyricProgressBar.Maximum : 0;
            PositionSeekThumb(ratio, trackWidth);
        }

        private void PositionSeekThumb(double ratio, double trackWidth)
        {
            double iconX = Math.Clamp(ratio * trackWidth - SeekThumbIcon.Width / 2, 0, Math.Max(0, trackWidth - SeekThumbIcon.Width));
            Canvas.SetLeft(SeekThumbIcon, iconX);
        }
    }
}
