using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace EventGhostDotNet.Platform.Windows;

/// <summary>
/// Inscription de l'application au démarrage de Windows : valeur nommée dans
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>, lancée avec <c>--minimized</c>.
/// </summary>
internal static class WindowsStartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "EventGhostDotNet";
    private const string MinimizedArgument = "--minimized";

    /// <summary>La clé de démarrage contient-elle une valeur pour cette application ?</summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public static void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            using var existing = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            existing?.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Impossible d'ouvrir la clé de démarrage de Windows.");
        key.SetValue(ValueName, BuildCommandLine(), RegistryValueKind.String);
    }

    internal static string BuildCommandLine()
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Chemin de l'application introuvable.");
        // Lancé par `dotnet EventGhostDotNet.dll`, le processus est l'hôte .NET et non l'application.
        if (string.Equals(Path.GetFileName(processPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase) &&
            Assembly.GetEntryAssembly()?.Location is { Length: > 0 } assemblyPath)
            return $"\"{processPath}\" \"{assemblyPath}\" {MinimizedArgument}";
        return $"\"{processPath}\" {MinimizedArgument}";
    }

    /// <summary>Un lancement par Windows demande-t-il un démarrage minimisé ?</summary>
    public static bool RequestsMinimizedStartup(IEnumerable<string> arguments) =>
        arguments.Any(argument =>
            string.Equals(argument, MinimizedArgument, StringComparison.OrdinalIgnoreCase));
}
