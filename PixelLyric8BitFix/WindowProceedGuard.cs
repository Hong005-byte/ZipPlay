using System.Windows;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// Splash → Welcome → Home 这条链上的窗口共用的"跳到下一个窗口就不用退出整个 App"逻辑：App 是
    /// ShutdownMode="OnExplicitShutdown"，每个窗口不管是正常跳到下一步、还是被用户直接关掉（标题栏 X /
    /// Alt+F4），都得自己决定要不要退出整个进程——只有"没有正常往下走"才需要退出。这段逻辑之前在
    /// SplashWindow/WelcomeWindow/HomeWindow 里各自抄了一遍（MainWindow 那份因为 Closed 事件里还夹着一大堆
    /// 跟这个逻辑无关的清理工作——SMTC 解绑、托盘图标释放、热键注销——没有一并收进来，继续留在
    /// MainWindow.xaml.cs 自己那份 _navigatingToSettings 字段里，避免把两件不相关的事强行绑在一起），
    /// 抽成这一个小助手之后，新窗口只要在构造函数里 new 一个、跳转的时候调 ProceedTo 就行，不用重新抄一遍
    /// "记标记 + Closed 时按标记判断要不要 Shutdown"这套。
    /// </summary>
    internal sealed class WindowProceedGuard
    {
        private readonly Window _owner;
        private bool _proceeding;

        public WindowProceedGuard(Window owner)
        {
            _owner = owner;
            owner.Closed += (s, e) => { if (!_proceeding) Application.Current.Shutdown(); };
        }

        /// <summary>正常跳到下一个窗口：显示新窗口、设成当前 Application.MainWindow、关掉自己——
        /// 调用这个方法本身就代表"这是一次正常的跳转"，Closed 事件里不会触发上面的 Shutdown。</summary>
        public void ProceedTo(Window next)
        {
            _proceeding = true;
            Application.Current.MainWindow = next;
            next.Show();
            _owner.Close();
        }
    }
}
