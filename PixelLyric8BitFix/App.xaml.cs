using System;
using System.Configuration;
using System.Data;
using System.Threading;
using System.Windows;

namespace PixelLyric8BitFix;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    // 单实例互斥锁：名字里带上 installer/ZipPlay.iss 里那个 AppId 的 GUID，跟安装包的 AppMutex 设置
    // 保持一致——这样"应用内一键更新"重装的时候，Inno Setup 才能检测到旧进程还占着这把锁，
    // 主动等它/关它，而不是安装完成但旧进程还缩在托盘里没退，新旧两个进程叠在一起，
    // 用户点到的还是旧的那个，看起来就像"明明更新了、版本号却没变、还一直提示更新"。
    // 这里再加一道保险：万一旧进程真的还活着，新启动的这个直接让路退出，不跟旧的抢。
    private static Mutex? _singleInstanceMutex;
    private const string SingleInstanceMutexName = "ZipPlay_SingleInstance_73EB0A1A-DB8A-4ADF-B4ED-C58002CE5C9F";

    public App()
    {
        // 兜底记录两类"本来会直接崩溃、之前完全没留下任何线索"的异常，写到 AppLog.LogPath。
        // 只记录，不吞掉——记完之后仍按默认行为处理（该崩还是崩），避免异常发生后
        // 程序带着已经坏掉的内部状态硬撑着继续跑，那种情况往往比直接崩溃更难排查。
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex) AppLog.Error("AppDomain.UnhandledException", ex);
        };

        DispatcherUnhandledException += (s, e) =>
        {
            AppLog.Error("DispatcherUnhandledException", e.Exception);
        };
    }

    // 拿到锁的算第一个实例，正常往下走（base.OnStartup 会按 App.xaml 里的 StartupUri 打开启动画面）；
    // 拿不到锁说明已经有一个实例在跑了（很可能缩在托盘里，用户没注意到），这个新开的直接让路退出，
    // 不调 base.OnStartup，启动画面/主窗口都不会创建出来，也就不会跟旧实例叠成两个托盘图标
    protected override void OnStartup(StartupEventArgs e)
    {
        bool createdNew;
        try
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, name: SingleInstanceMutexName, out createdNew);
        }
        catch (UnauthorizedAccessException)
        {
            // 这把互斥锁曾经被一个权限更高的实例（比如被右键"以管理员身份运行"过一次）创建并且还活着——
            // 同名但权限对不上，new Mutex 直接抛异常，不是 createdNew=false 那种"正常检测到已有实例"的路径。
            // 语义上这仍然是"已经有实例在跑"，用户不需要看到一次崩溃，跟下面 createdNew=false 走一样的处理：
            // 让路退出，不重复开窗口。
            AppLog.Info("App: 检测到已有实例在跑（权限不同，Mutex 创建被拒绝），这次启动直接退出");
            Shutdown();
            return;
        }

        if (!createdNew)
        {
            AppLog.Info("App: 已有一个实例在跑，这次启动直接退出，不重复开窗口");
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }
}

