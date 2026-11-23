using System.IO;
using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace EventGhostDotNet;

public partial class App : Application
{
    private static readonly object ErrorLogLock = new();
    private SingleInstanceGuard? _singleInstance;
    private NotificationService? _notifications;

    internal static string ErrorLogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EventGhostDotNet",
        "errors.log");

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = SingleInstanceGuard.TryAcquire(() =>
            Dispatcher.BeginInvoke(new Action(() => (MainWindow as MainWindow)?.ShowFromNotification())));
        if (_singleInstance is null)
        {
            Shutdown();
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    internal void ShowNotification(string title, string message, string? key = null) =>
        (_notifications ??= new NotificationService(Dispatcher)).Show(title, message, key,
            () => (MainWindow as MainWindow)?.ShowFromNotification());

    internal void ShowNotificationHistory()
    {
        var notifications = _notifications ??= new NotificationService(Dispatcher);
        var window = new NotificationHistoryWindow(notifications.History.ToArray(),
            () => (MainWindow as MainWindow)?.ShowFromNotification());
        if (MainWindow is { IsVisible: true } owner)
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _notifications?.Dispose();
        _notifications = null;
        _singleInstance?.Dispose();
        _singleInstance = null;
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError(e.Exception, "Exception non gérée (interface)");
        e.Handled = true;

        var windowMissing = MainWindow is null;
        try
        {
            MessageBox.Show(
                $"Une erreur inattendue s'est produite.\n\n{e.Exception.Message}\n\nDétails : {ErrorLogPath}",
                "EventGhost .NET", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception exception) when (exception is InvalidOperationException or
                                           System.ComponentModel.Win32Exception)
        {
        }

        if (windowMissing)
            Shutdown(1);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            LogError(exception, "Exception non gérée (processus)");
        else
            LogError(new InvalidOperationException(e.ExceptionObject?.ToString() ?? "Exception inconnue"),
                "Exception non gérée (processus)");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogError(e.Exception, "Exception de tâche non observée");
        e.SetObserved();
    }

    internal static void LogError(Exception exception, string source)
    {
        try
        {
            var directory = Path.GetDirectoryName(ErrorLogPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            lock (ErrorLogLock)
            {
                var file = new FileInfo(ErrorLogPath);
                if (file.Exists && file.Length > 1_000_000)
                    File.Move(ErrorLogPath, ErrorLogPath + ".old", true);
                File.AppendAllText(ErrorLogPath,
                    $"[{DateTimeOffset.Now:O}] {source}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch (Exception logException) when (logException is IOException or UnauthorizedAccessException or
                                               NotSupportedException or System.Security.SecurityException)
        {
            // Le journal ne doit jamais faire échouer le gestionnaire d'exceptions.
        }
    }
}
