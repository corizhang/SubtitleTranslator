using System.Windows;
using System.Windows.Threading;

namespace SubtitleTranslator.App;

public partial class App : System.Windows.Application
{
    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        AppFileLogger.Info($"应用启动，版本 {GetType().Assembly.GetName().Version}。");
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_OnUnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_OnUnobservedTaskException;
    }

    private void App_OnExit(object sender, ExitEventArgs e) => AppFileLogger.Info($"应用退出，代码 {e.ApplicationExitCode}。");

    private void App_OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppFileLogger.Error("UI 线程发生未处理异常。", e.Exception);
        e.Handled = true;
        ErrorDialogWindow.ShowFatal(e.Exception);
        Shutdown(-1);
    }

    private static void CurrentDomain_OnUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        AppFileLogger.Error("运行时发生未处理异常。", e.ExceptionObject as Exception);

    private static void TaskScheduler_OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppFileLogger.Error("后台任务发生未观察异常。", e.Exception);
        e.SetObserved();
    }
}
