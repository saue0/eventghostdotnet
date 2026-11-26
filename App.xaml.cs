using System.IO;
using System.Windows;
using System.Windows.Threading;
using EventGhostDotNet.Platform.Windows;
using EventGhostDotNet.PluginContracts;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace EventGhostDotNet;

public partial class App : Application
{
    private const string SmokeTestArgument = "--smoke-test";

    private SingleInstanceGuard? _singleInstance;
    private NotificationService? _notifications;

    internal static string ErrorLogPath => DiagnosticLog.ErrorLogPath;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(argument =>
                string.Equals(argument, SmokeTestArgument, StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(RunSmokeTest());
            return;
        }

        Theme.AppTheme.Apply();

        _singleInstance = SingleInstanceGuard.TryAcquire(() =>
            Dispatcher.BeginInvoke(new Action(() => (MainWindow as MainWindow)?.ShowFromNotification())));
        if (_singleInstance is null)
        {
            Shutdown();
            return;
        }

        var window = new MainWindow(WindowsStartupRegistration.RequestsMinimizedStartup(e.Args));
        MainWindow = window;
        window.Show();
    }

    /// <summary>
    /// Mode diagnostic sans interface, utilisé par la CI : charge les plugins du dossier de
    /// l'application, écrit le résultat sur la sortie standard et sort avec 0 en cas de succès.
    /// </summary>
    private static int RunSmokeTest()
    {
        var runtime = new PluginRuntime();
        var errors = 0;
        runtime.Load(Path.Combine(AppContext.BaseDirectory, "Plugins"), (message, level) =>
        {
            if (level == LogLevel.Error)
                errors++;
            Console.WriteLine($"{level} : {message}");
        });

        var plugins = runtime.Plugins
            .Select(plugin => plugin.Id)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Console.WriteLine($"Plugins chargés : {plugins.Length} ({string.Join(", ", plugins)})");
        Console.Out.Flush();

        if (plugins.Length == 0)
            Console.Error.WriteLine("Aucun plugin n'a été chargé.");
        return errors == 0 && plugins.Length > 0 ? 0 : 1;
    }

    internal void ShowNotification(string title, string message, string? key = null) =>
        (_notifications ??= new NotificationService(Dispatcher)).Show(title, message, key,
            () => (MainWindow as MainWindow)?.ShowFromNotification());

    internal void ShowNotificationHistory()
    {
        var notifications = _notifications ??= new NotificationService(Dispatcher);
        var window = new NotificationHistoryWindow(notifications.History.ToArray(),
            entry => notifications.Remove(entry),
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
        const string source = "Exception non gérée (processus)";
        var exception = e.ExceptionObject as Exception ??
                        new InvalidOperationException(e.ExceptionObject?.ToString() ?? "Exception inconnue");
        LogError(exception, source);
        if (e.IsTerminating)
            WindowsEventLog.WriteFatal(source, exception, ErrorLogPath);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogError(e.Exception, "Exception de tâche non observée");
        e.SetObserved();
    }

    internal static void LogError(Exception exception, string source) =>
        DiagnosticLog.WriteError(DateTimeOffset.Now, source, exception);
}
