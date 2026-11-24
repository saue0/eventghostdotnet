namespace EventGhostDotNet.Core.PowerShell;

/// <summary>
/// Localise PowerShell 7 pour les actions de script : le chemin configuré est prioritaire,
/// puis les dossiers du <c>PATH</c> et les emplacements d'installation habituels.
/// </summary>
public static class PowerShellExecutables
{
    public const string PowerShell7FileName = "pwsh.exe";

    /// <summary>Chemin de Windows PowerShell 5.1 fourni avec Windows.</summary>
    public static string WindowsPowerShellPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "WindowsPowerShell",
        "v1.0",
        "powershell.exe");

    public static string? FindPowerShell7(string? configuredPath,
        IEnumerable<string>? searchDirectories = null, bool includeKnownInstallLocations = true)
    {
        foreach (var candidate in EnumerateCandidates(configuredPath, searchDirectories, includeKnownInstallLocations))
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        return null;
    }

    /// <summary>
    /// Nettoie un chemin configuré et le complète avec <c>pwsh.exe</c> lorsqu'il désigne un dossier.
    /// Retourne <c>null</c> pour une valeur vide, ce qui active la détection automatique.
    /// </summary>
    public static string? NormalizeConfiguredPath(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return null;
        var path = configuredPath.Trim().Trim('"');
        if (path.Length == 0)
            return null;
        return Directory.Exists(path) ? Path.Combine(path, PowerShell7FileName) : path;
    }

    public static IEnumerable<string> EnumerateCandidates(string? configuredPath,
        IEnumerable<string>? searchDirectories = null, bool includeKnownInstallLocations = true)
    {
        var configured = NormalizeConfiguredPath(configuredPath);
        if (configured is not null)
            yield return configured;

        var directories = searchDirectories ??
            (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var directory in directories)
        {
            var trimmed = directory.Trim().Trim('"');
            if (trimmed.Length > 0)
                yield return Path.Combine(trimmed, PowerShell7FileName);
        }

        if (!includeKnownInstallLocations)
            yield break;

        foreach (var folder in new[]
                 {
                     Environment.SpecialFolder.ProgramFiles,
                     Environment.SpecialFolder.ProgramFilesX86
                 })
        {
            var root = Environment.GetFolderPath(folder);
            if (string.IsNullOrEmpty(root))
                continue;
            yield return Path.Combine(root, "PowerShell", "7", PowerShell7FileName);
            yield return Path.Combine(root, "PowerShell", "7-preview", PowerShell7FileName);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localAppData))
            yield return Path.Combine(localAppData, "Microsoft", "WindowsApps", PowerShell7FileName);
    }
}
