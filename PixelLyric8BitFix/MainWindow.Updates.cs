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
    // 检查更新 + 一键下载安装。查版本用的是主 _httpClient（小请求，4 秒超时足够），
    // 下安装包用的是 _downloadHttpClient（大文件，超时给得长很多），两个不能混用，见 MainWindow.xaml.cs 里的注释。
    public partial class MainWindow : Window
    {
        // 启动几秒后在后台悄悄查一次有没有新版本，查不到 / 没网络都不影响正常使用
        private async Task CheckForUpdateInBackgroundAsync()
        {
            var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            var info = await UpdateChecker.CheckForUpdateAsync(currentVersion, _httpClient);
            if (info == null) return;

            Dispatcher.Invoke(() =>
            {
                _updateInfo = info;
                TxtUpdateBadge.Text = $"🎉 新版本 v{info.Version}";
                UpdateBadge.Visibility = Visibility.Visible;
            });
        }

        // 点新版本徽标：一键下载安装包 + 静默装上 + 自动重启进新版本；
        // 找不到安装包直链时退回成打开浏览器手动下载，不会卡住什么都不做
        private async void UpdateBadge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (_updateInProgress || _updateInfo == null) return;

            if (string.IsNullOrEmpty(_updateInfo.InstallerDownloadUrl))
            {
                try { Process.Start(new ProcessStartInfo(_updateInfo.ReleaseUrl) { UseShellExecute = true }); } catch { }
                return;
            }

            _updateInProgress = true;
            TxtUpdateBadge.Text = "⬇ 下载中... 0%";

            try
            {
                var progress = new Progress<double>(p =>
                {
                    TxtUpdateBadge.Text = $"⬇ 下载中... {(int)(p * 100)}%";
                });

                string installerPath = await UpdateChecker.DownloadInstallerAsync(
                    _updateInfo.InstallerDownloadUrl, _downloadHttpClient, progress, CancellationToken.None);

                TxtUpdateBadge.Text = "✅ 正在安装...";
                UpdateChecker.LaunchInstallerAndExit(installerPath);
            }
            catch (Exception ex)
            {
                _updateInProgress = false;
                TxtUpdateBadge.Text = "⚠️ 下载失败，点击重试";
                AppLog.Error("UpdateBadge download/install", ex);
            }
        }
    }
}
