using System.ComponentModel;
using System.Diagnostics;
using System.Security;

namespace EventGhostDotNet.Platform.Windows;

/// <summary>
/// Entrée du journal des événements Windows en cas d'exception fatale. La source dédiée est
/// enregistrée par l'installateur ; sans elle (exécution depuis les sources ou en portable),
/// l'entrée est écrite avec la source générique « Application ».
/// </summary>
internal static class WindowsEventLog
{
    private const string SourceName = "EventGhostDotNet";
    private const string FallbackSourceName = "Application";
    private const int MaximumMessageLength = 30_000;

    public static void WriteFatal(string source, Exception exception, string logPath)
    {
        var message = $"{source}{Environment.NewLine}{Environment.NewLine}{exception}" +
                      $"{Environment.NewLine}{Environment.NewLine}Journal : {logPath}";
        if (message.Length > MaximumMessageLength)
            message = message[..MaximumMessageLength];

        if (TryWrite(SourceName, message))
            return;
        TryWrite(FallbackSourceName, message);
    }

    private static bool TryWrite(string source, string message)
    {
        try
        {
            EventLog.WriteEntry(source, message, EventLogEntryType.Error);
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or
                                           SecurityException or UnauthorizedAccessException or
                                           Win32Exception)
        {
            return false;
        }
    }
}
