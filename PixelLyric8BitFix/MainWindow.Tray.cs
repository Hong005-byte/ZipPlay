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
    // 系统托盘图标 + 全局热键（Ctrl+Alt+L）。两个放一个文件是因为都是"就算窗口被藏起来也要有办法拉回来"这同一件事。
    public partial class MainWindow : Window
    {
        // 托盘图标：直接复用程序自己的 exe 图标，双击/菜单里的"显示"都能把窗口拉回来。
        // 搭不起来（比如某些精简系统组件缺失）不该拖累主功能，静默跳过，但记一笔方便排查
        private void InitTrayIcon()
        {
            try
            {
                string? exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                _trayIconManager.Init(exePath, "ZipPlay - 像素歌词浮窗", ShowFromTray, OpenSettingsAndClose, () => Application.Current.Shutdown());
            }
            catch (Exception ex)
            {
                AppLog.Error("InitTrayIcon", ex);
            }
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        // 窗口句柄真正建好之后才能注册全局热键；注册失败（比如 Ctrl+Alt+L 已经被别的程序占了）
        // 不影响正常使用，只是热键这一条路走不通，鼠标/托盘那几条路照样能用
        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            try
            {
                var handle = new WindowInteropHelper(this).Handle;
                _hwndSource = HwndSource.FromHwnd(handle);
                _hwndSource?.AddHook(WndProc);
                RegisterHotKey(handle, HotkeyId, ModControl | ModAlt, VkL);
            }
            catch (Exception ex)
            {
                AppLog.Error("RegisterHotKey", ex);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                ToggleVisibilityHotkey();
                handled = true;
            }
            return IntPtr.Zero;
        }

        // 全局热键只管"显示/隐藏"这一个开关，不去搅和 Mini 模式的状态——
        // 隐藏了就拉回来（顺便置顶抢一下焦点），没隐藏（不管是完整状态还是 Mini 状态）就收进托盘
        private void ToggleVisibilityHotkey()
        {
            if (!IsVisible) ShowFromTray();
            else Hide();
        }
    }
}
