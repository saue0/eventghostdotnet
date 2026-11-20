using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Windows;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Application = System.Windows.Application;

namespace EventGhostDotNet;

public partial class App : Application
{
    private AppNotificationManager? _notificationManager;
    private Exception? _notificationError;

    internal string? NotificationError => _notificationError?.Message;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                throw new InvalidOperationException("Les notifications Windows ne sont pas prises en charge en mode administrateur.");

            if (!AppNotificationManager.IsSupported())
                throw new InvalidOperationException("Les notifications Windows ne sont pas disponibles.");

            var manager = AppNotificationManager.Default;
            manager.NotificationInvoked += NotificationInvoked;
            try
            {
                manager.Register();
                _notificationManager = manager;
            }
            finally
            {
                if (_notificationManager is null)
                    manager.NotificationInvoked -= NotificationInvoked;
            }
        }
        catch (Exception exception) when (exception is COMException or FileNotFoundException or DllNotFoundException or
                                           InvalidOperationException or
                                           UnauthorizedAccessException or NotSupportedException or
                                           TypeInitializationException or System.ComponentModel.Win32Exception)
        {
            _notificationError = exception;
        }
    }

    internal void ShowNotification(string title, string message, string? key = null)
    {
        if (_notificationManager is null)
            throw new InvalidOperationException(
                $"Les notifications Windows ne sont pas disponibles : {NotificationError ?? "initialisation non effectuée"}",
                _notificationError);

        try
        {
            var setting = _notificationManager.Setting;
            if (setting != AppNotificationSetting.Enabled)
                throw new InvalidOperationException($"Les notifications Windows sont désactivées : {setting}.");

            var notification = new AppNotificationBuilder()
                .AddArgument("action", "show")
                .AddText(title)
                .AddText(message)
                .MuteAudio()
                .BuildNotification();
            if (key is not null)
                notification.Tag = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
            _notificationManager.Show(notification);
        }
        catch (Exception exception) when (exception is COMException or UnauthorizedAccessException or
                                           NotSupportedException or System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException($"Impossible d'envoyer la notification Windows : {exception.Message}", exception);
        }
    }

    private void NotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args) =>
        Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.ShowFromNotification(),
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);

    protected override void OnExit(ExitEventArgs e)
    {
        if (_notificationManager is not null)
        {
            _notificationManager.Unregister();
            _notificationManager.NotificationInvoked -= NotificationInvoked;
        }
        base.OnExit(e);
    }
}
