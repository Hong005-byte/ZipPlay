using System;
using Forms = System.Windows.Forms; // 只用来做系统托盘图标，别名避免跟 System.Windows.Controls 的同名类型撞车

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 系统托盘图标的搭建/销毁全封在这一个类里，从 MainWindow 里搬出来——
    /// 托盘这块跟"歌词怎么同步/皮肤怎么画"完全是两件事，独立成类之后 MainWindow 不用再管
    /// NotifyIcon/Icon 这两个 IDisposable 资源的生命周期，直接 using 或者 Dispose 这一个对象就够了。
    /// </summary>
    internal sealed class TrayIconManager : IDisposable
    {
        private Forms.NotifyIcon? _icon;
        private System.Drawing.Icon? _iconHandle;

        /// <summary>
        /// 搭建托盘图标。搭不起来（比如某些精简系统组件缺失）不该拖累主功能，
        /// 调用方应该把这个方法包在 try/catch 里，失败就当没有托盘图标继续跑。
        /// </summary>
        public void Init(string? exePath, string tooltip, Action onShow, Action onOpenSettings, Action onExit)
        {
            _iconHandle = !string.IsNullOrEmpty(exePath)
                ? System.Drawing.Icon.ExtractAssociatedIcon(exePath)
                : System.Drawing.SystemIcons.Application;

            var trayMenu = new Forms.ContextMenuStrip();
            trayMenu.Items.Add("显示 ZipPlay", null, (s, e) => onShow());
            trayMenu.Items.Add("⚙ 更改皮肤 / 设置", null, (s, e) => { onShow(); onOpenSettings(); });
            trayMenu.Items.Add(new Forms.ToolStripSeparator());
            trayMenu.Items.Add("✕ 退出", null, (s, e) => onExit());

            _icon = new Forms.NotifyIcon
            {
                Icon = _iconHandle,
                Text = tooltip,
                ContextMenuStrip = trayMenu,
                Visible = true,
            };
            _icon.DoubleClick += (s, e) => onShow();
        }

        public void Dispose()
        {
            _icon?.Dispose();
            _icon = null;
            _iconHandle?.Dispose();
            _iconHandle = null;
        }
    }
}
