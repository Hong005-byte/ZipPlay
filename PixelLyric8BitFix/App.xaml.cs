using System;
using System.Configuration;
using System.Data;
using System.Windows;

namespace PixelLyric8BitFix;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
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
}

