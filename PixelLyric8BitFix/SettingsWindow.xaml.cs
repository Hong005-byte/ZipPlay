using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 启动设置页：不是账号登录，只是让用户在打开主播放器窗口之前
    /// 选好皮肤 / 尺寸 / 显示模式，选择结果落地为本地配置文件。
    /// </summary>
    public partial class SettingsWindow : Window
    {
        // App 用的是 ShutdownMode="OnExplicitShutdown"，所以这个窗口无论怎么被关掉
        // （点"开始播放"、或者直接按标题栏的 X）都要自己决定：是继续流程，还是退出整个 App。
        private bool _proceeding = false;

        private readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };

        // 查版本（小 JSON 请求）跟下安装包（几十 MB 的文件）不能共用同一个短超时的 HttpClient——
        // HttpClient.Timeout 管的是整个请求（包括读响应体），4~6 秒对一个大文件下载来说太容易半路被打断。
        // 详见 MainWindow.xaml.cs 里同样的两个 HttpClient 的注释。
        private readonly HttpClient _downloadHttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        private UpdateInfo? _foundUpdate;
        private bool _updateInProgress;

        // 客制化主题卡片是动态加到 SkinPanel 末尾的（不是 XAML 写死的那 17 张），记着引用方便刷新时先摘掉旧的
        private readonly List<RadioButton> _customThemeCards = new();

        public SettingsWindow()
        {
            InitializeComponent();

            // GitHub API 强制要求请求带 User-Agent，不带会直接 403——之前漏配过这个，
            // 403 被 UpdateChecker 当成"没有更新"处理，导致明明有新版本却显示"已是最新版本"
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ZipPlay-UpdateChecker");
            _downloadHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ZipPlay-UpdateChecker");

            Closed += (s, e) => _downloadHttpClient.Dispose();

            Closed += (s, e) =>
            {
                if (!_proceeding) Application.Current.Shutdown();
            };

            var settings = AppSettings.Load();

            foreach (var child in SkinPanel.Children)
            {
                if (child is RadioButton rb && rb.Tag is string tag && Enum.TryParse<PlayerSkin>(tag, out var skin))
                {
                    rb.IsChecked = skin == settings.Skin;
                }
            }
            RefreshCustomThemeCards(settings);

            RbSizeSmall.IsChecked = settings.Size == PlayerSize.Small;
            RbSizeMedium.IsChecked = settings.Size == PlayerSize.Medium;
            RbSizeLarge.IsChecked = settings.Size == PlayerSize.Large;

            RbModeStandard.IsChecked = settings.DisplayMode == PlayerDisplayMode.Standard;
            RbModeMinimal.IsChecked = settings.DisplayMode == PlayerDisplayMode.Minimal;

            RbFontSmall.IsChecked = settings.FontSize == LyricFontSize.Small;
            RbFontMedium.IsChecked = settings.FontSize == LyricFontSize.Medium;
            RbFontLarge.IsChecked = settings.FontSize == LyricFontSize.Large;

            ChkMiniVisualizerEnabled.IsChecked = settings.MiniVisualizerEnabled;
            RbVisualizerLow.IsChecked = settings.VisualizerSensitivity == VisualizerSensitivity.Low;
            RbVisualizerMedium.IsChecked = settings.VisualizerSensitivity == VisualizerSensitivity.Medium;
            RbVisualizerHigh.IsChecked = settings.VisualizerSensitivity == VisualizerSensitivity.High;
            ChkSkinAudioReactiveEnabled.IsChecked = settings.SkinAudioReactiveEnabled;

            ChkSkipStartupSettings.IsChecked = settings.SkipStartupSettings;

            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            TxtCurrentVersion.Text = $"当前版本 v{currentVersion.ToString(3)}";

            RefreshCacheInfo();
        }

        // 客制化主题的卡片 Tag 是 "Custom:文件名" 这种复合格式（PlayerSkin.Custom 这一个枚举值被 5 个不同文件共用，
        // 光靠枚举名分不清是哪一个），内置皮肤还是走 Tag == 枚举名那套
        private const string CustomTagPrefix = "Custom:";

        private PlayerSkin GetSelectedSkin()
        {
            foreach (var child in SkinPanel.Children)
            {
                if (child is RadioButton { IsChecked: true } rb && rb.Tag is string tag)
                {
                    if (tag.StartsWith(CustomTagPrefix)) return PlayerSkin.Custom;
                    if (Enum.TryParse<PlayerSkin>(tag, out var skin)) return skin;
                }
            }
            return PlayerSkin.Minecraft;
        }

        private string? GetSelectedCustomThemeFile()
        {
            foreach (var child in SkinPanel.Children)
            {
                if (child is RadioButton { IsChecked: true } rb && rb.Tag is string tag && tag.StartsWith(CustomTagPrefix))
                {
                    return tag.Substring(CustomTagPrefix.Length);
                }
            }
            return null;
        }

        // 把上次加过的客制化卡片先摘掉，再按最新存的主题列表重新加一遍（新建/编辑/删除之后都要调用这个刷新）
        private void RefreshCustomThemeCards(AppSettings settings)
        {
            foreach (var old in _customThemeCards) SkinPanel.Children.Remove(old);
            _customThemeCards.Clear();

            foreach (var entry in CustomThemeStore.ListAll())
            {
                var rb = new RadioButton
                {
                    Style = (Style)FindResource("SkinCardStyle"),
                    GroupName = "Skin",
                    Tag = CustomTagPrefix + entry.FileName,
                    IsChecked = settings.Skin == PlayerSkin.Custom && settings.CustomThemeFile == entry.FileName,
                };

                CustomThemeValidator.TryParseHexColor(entry.Theme.Colors?.Accent ?? "#55FF55", out var accent);
                var stops = entry.Theme.Background?.Stops ?? new List<string>();
                Color c1 = accent, c2 = accent;
                if (stops.Count > 0) CustomThemeValidator.TryParseHexColor(stops[0], out c1);
                if (stops.Count > 0) CustomThemeValidator.TryParseHexColor(stops[^1], out c2);

                rb.Background = new LinearGradientBrush(c1, c2, 90);
                rb.BorderBrush = new SolidColorBrush(accent);
                rb.Content = new TextBlock { Text = "🎨 " + (entry.Theme.Name ?? "自定义"), Style = (Style)FindResource("SkinCardLabelStyle") };

                SkinPanel.Children.Add(rb);
                _customThemeCards.Add(rb);
            }
        }

        private void BtnManageCustomThemes_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new CustomThemeWindow { Owner = this };
            dlg.ShowDialog();
            if (dlg.ThemesChanged)
            {
                RefreshCustomThemeCards(AppSettings.Load());
            }
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            // 先读回磁盘上已有的设置（保留 WindowLeft/WindowTop 这些这一页没有 UI 控件管理的字段），
            // 只覆盖这一页选出来的三项。之前这里直接 new 一个全新的 AppSettings，
            // 会把 MainWindow 关闭时刚存的窗口位置直接清空，导致"记住位置"形同虚设。
            var settings = AppSettings.Load();
            settings.Skin = GetSelectedSkin();
            settings.CustomThemeFile = GetSelectedCustomThemeFile();
            settings.Size = RbSizeSmall.IsChecked == true ? PlayerSize.Small
                          : RbSizeLarge.IsChecked == true ? PlayerSize.Large
                          : PlayerSize.Medium;
            settings.DisplayMode = RbModeMinimal.IsChecked == true ? PlayerDisplayMode.Minimal : PlayerDisplayMode.Standard;
            settings.FontSize = RbFontSmall.IsChecked == true ? LyricFontSize.Small
                               : RbFontLarge.IsChecked == true ? LyricFontSize.Large
                               : LyricFontSize.Medium;
            settings.MiniVisualizerEnabled = ChkMiniVisualizerEnabled.IsChecked == true;
            settings.VisualizerSensitivity = RbVisualizerLow.IsChecked == true ? VisualizerSensitivity.Low
                                            : RbVisualizerHigh.IsChecked == true ? VisualizerSensitivity.High
                                            : VisualizerSensitivity.Medium;
            settings.SkinAudioReactiveEnabled = ChkSkinAudioReactiveEnabled.IsChecked == true;
            settings.SkipStartupSettings = ChkSkipStartupSettings.IsChecked == true;
            settings.Save();

            _proceeding = true;
            var main = new MainWindow(settings);
            Application.Current.MainWindow = main;
            main.Show();

            Close();
        }

        // 手动检查更新：不用等下次启动主窗口的后台检查，点一下马上就知道结果
        private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckUpdate.IsEnabled = false;
            TxtUpdateStatus.Visibility = Visibility.Visible;
            TxtUpdateStatus.Cursor = Cursors.Arrow;
            TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
            TxtUpdateStatus.Text = "🔄 检查中...";
            _foundUpdate = null;

            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            var result = await UpdateChecker.CheckAsync(currentVersion, _httpClient);

            if (!result.Success)
            {
                TxtUpdateStatus.Text = "⚠️ 检查失败，请确认网络连接后重试";
                TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x88, 0x88));
            }
            else if (result.Update != null)
            {
                _foundUpdate = result.Update;
                bool canOneClick = !string.IsNullOrEmpty(result.Update.InstallerDownloadUrl);
                TxtUpdateStatus.Text = canOneClick
                    ? $"🎉 发现新版本 v{result.Update.Version}，点击这里立即更新"
                    : $"🎉 发现新版本 v{result.Update.Version}，点击这里前往下载";
                TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0xFF, 0x55));
                TxtUpdateStatus.Cursor = Cursors.Hand;
            }
            else
            {
                TxtUpdateStatus.Text = "✅ 已经是最新版本";
                TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
            }

            BtnCheckUpdate.IsEnabled = true;
        }

        // 有安装包直链就一键下载 + 静默装 + 自动重启进新版本；没有直链就退回打开浏览器
        private async void TxtUpdateStatus_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_updateInProgress || _foundUpdate == null) return;

            if (string.IsNullOrEmpty(_foundUpdate.InstallerDownloadUrl))
            {
                try { Process.Start(new ProcessStartInfo(_foundUpdate.ReleaseUrl) { UseShellExecute = true }); } catch { }
                return;
            }

            _updateInProgress = true;
            TxtUpdateStatus.Cursor = Cursors.Arrow;
            TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
            TxtUpdateStatus.Text = "⬇ 下载中... 0%";

            try
            {
                var progress = new Progress<double>(p =>
                {
                    TxtUpdateStatus.Text = $"⬇ 下载中... {(int)(p * 100)}%";
                });

                string installerPath = await UpdateChecker.DownloadInstallerAsync(
                    _foundUpdate.InstallerDownloadUrl, _downloadHttpClient, progress, CancellationToken.None);

                TxtUpdateStatus.Text = "✅ 正在安装...";
                _proceeding = true; // 接下来是主动退出去装新版本，不是意外关闭
                UpdateChecker.LaunchInstallerAndExit(installerPath);
            }
            catch (Exception ex)
            {
                _updateInProgress = false;
                TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x88, 0x88));
                TxtUpdateStatus.Text = "⚠️ 下载失败，点击重试";
                TxtUpdateStatus.Cursor = Cursors.Hand;
                AppLog.Error("SettingsWindow update download/install", ex);
            }
        }

        // 显示当前缓存了多少首歌词、占多大空间，清空之后重新算一遍
        private void RefreshCacheInfo()
        {
            var (count, bytes) = LyricsCache.GetStats();
            TxtCacheInfo.Text = count == 0 ? "暂无缓存" : $"已缓存 {count} 首歌词，共 {FormatBytes(bytes)}";
        }

        private static string FormatBytes(long bytes) =>
            bytes < 1024 ? $"{bytes} B" :
            bytes < 1024 * 1024 ? $"{bytes / 1024.0:F1} KB" :
            $"{bytes / 1024.0 / 1024.0:F1} MB";

        private void BtnClearCache_Click(object sender, RoutedEventArgs e)
        {
            LyricsCache.Clear();
            RefreshCacheInfo();
        }

        // 让用户自己在资源管理器里看一眼这个文件夹，眼见为实：清空按钮只会动这一个专属子目录
        private void BtnOpenCacheFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(LyricsCache.CacheDir); // 还没缓存过任何歌词时文件夹不存在，先建出来再打开
                Process.Start(new ProcessStartInfo(LyricsCache.CacheDir) { UseShellExecute = true });
            }
            catch
            {
                // 打不开（比如权限问题）不是关键功能，静默失败就好
            }
        }

        // 日志文件还没生成过（一切正常，从来没触发过 AppLog.Error）就退回打开父目录，不会因为文件不存在而报错
        private void BtnOpenLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (File.Exists(AppLog.LogPath))
                {
                    Process.Start(new ProcessStartInfo(AppLog.LogPath) { UseShellExecute = true });
                }
                else
                {
                    string? dir = Path.GetDirectoryName(AppLog.LogPath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
                    }
                }
            }
            catch
            {
                // 打不开不是关键功能，静默失败就好
            }
        }
    }
}
