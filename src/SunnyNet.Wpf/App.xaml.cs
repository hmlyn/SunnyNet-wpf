using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace SunnyNet.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        base.OnStartup(eventArgs);

        try
        {
            MainWindow = new MainWindow();
            MainWindow.Show();
        }
        catch (Exception exception)
        {
            LogException("Startup", exception);
            MessageBox.Show(exception.Message, "SunnyNet 启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs eventArgs)
    {
        LogException("Dispatcher", eventArgs.Exception);
        MessageBox.Show(eventArgs.Exception.Message, "SunnyNet 运行异常", MessageBoxButton.OK, MessageBoxImage.Error);
        eventArgs.Handled = true;
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs eventArgs)
    {
        if (eventArgs.ExceptionObject is Exception exception)
        {
            LogException("Unhandled", exception);
        }
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs eventArgs)
    {
        LogException("TaskScheduler", eventArgs.Exception);
        eventArgs.SetObserved();
    }

    private static void LogException(string source, Exception exception)
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string logDirectory = Path.Combine(appData, "SunnyNet.Wpf", "logs");
            Directory.CreateDirectory(logDirectory);
            string logPath = Path.Combine(logDirectory, "crash.log");
            File.AppendAllText(
                logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
