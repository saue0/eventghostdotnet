using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using EventGhostDotNet.Core.Diagnostics;
using EventGhostDotNet.PluginContracts;

namespace EventGhostDotNet;

/// <summary>
/// Journaux de diagnostic de l'application : le journal complet et les erreurs inattendues
/// sont écrits dans <c>%LOCALAPPDATA%\EventGhostDotNet\logs</c>, avec rotation par taille.
/// </summary>
internal static class DiagnosticLog
{
    private static readonly Regex AnsiSequences = new(@"\x1b\[[0-9;]*m",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EventGhostDotNet",
        "logs");

    public static string ApplicationLogPath => Path.Combine(LogDirectory, "eventghost.log");

    public static string ErrorLogPath => Path.Combine(LogDirectory, "errors.log");

    private static readonly RotatingLogFile ApplicationLog = new(ApplicationLogPath);
    private static readonly RotatingLogFile ErrorLog = new(ErrorLogPath);

    /// <summary>Ajoute une entrée du journal de l'interface au journal fichier complet.</summary>
    public static void Write(DateTime timestamp, LogLevel level, string category, int indentLevel, string message)
    {
        var indentation = new string(' ', Math.Clamp(indentLevel, 0, 64) * 2);
        var line = string.Concat(
            timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
            " [", level, "] [", category, "] ",
            indentation,
            AnsiSequences.Replace(message, string.Empty));
        WriteSafely(ApplicationLog, line);
    }

    /// <summary>Dernières lignes du journal complet, pour l'aperçu dans l'application.</summary>
    public static IReadOnlyList<string> ReadTail(int maximumLines) =>
        ApplicationLog.ReadTail(maximumLines);

    /// <summary>Supprime les journaux courants et leurs archives ; renvoie faux si un fichier résiste.</summary>
    public static bool Clear() =>
        ClearSafely(ApplicationLog) & ClearSafely(ErrorLog);

    /// <summary>Consigne une exception inattendue dans le journal des erreurs.</summary>
    public static void WriteError(DateTimeOffset timestamp, string source, Exception exception)
    {
        var line = string.Join(Environment.NewLine,
            $"[{timestamp.ToString("O", CultureInfo.InvariantCulture)}] {source}",
            exception.ToString());
        WriteSafely(ErrorLog, line);
    }

    private static bool ClearSafely(RotatingLogFile log)
    {
        try
        {
            log.Clear();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           NotSupportedException or ArgumentException or
                                           System.Security.SecurityException)
        {
            return false;
        }
    }

    private static void WriteSafely(RotatingLogFile log, string line)
    {
        try
        {
            log.Write(line);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           NotSupportedException or ArgumentException or
                                           System.Security.SecurityException)
        {
            // Un journal indisponible ne doit jamais faire échouer l'application.
        }
    }
}
