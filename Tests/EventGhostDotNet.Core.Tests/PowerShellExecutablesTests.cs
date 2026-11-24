using EventGhostDotNet.Core.PowerShell;
using Xunit;

namespace EventGhostDotNet.Core.Tests;

public sealed class PowerShellExecutablesTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "egdn-tests", Guid.NewGuid().ToString("N"));

    public PowerShellExecutablesTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Le_chemin_configure_prime_sur_les_dossiers_recherches()
    {
        var configured = CreatePowerShell7("configure");
        var searched = CreatePowerShell7("path");

        var found = PowerShellExecutables.FindPowerShell7(
            configured, [Path.GetDirectoryName(searched)!], includeKnownInstallLocations: false);

        Assert.Equal(Path.GetFullPath(configured), found);
    }

    [Fact]
    public void Un_dossier_configure_est_resolu_vers_pwsh_exe()
    {
        var directory = Path.GetDirectoryName(CreatePowerShell7("dossier"))!;

        var found = PowerShellExecutables.FindPowerShell7(
            directory, [], includeKnownInstallLocations: false);

        Assert.Equal(Path.GetFullPath(Path.Combine(directory, "pwsh.exe")), found);
    }

    [Fact]
    public void Le_path_est_utilise_quand_aucun_chemin_n_est_configure()
    {
        var searched = CreatePowerShell7("path");

        var found = PowerShellExecutables.FindPowerShell7(
            null, [Path.GetDirectoryName(searched)!], includeKnownInstallLocations: false);

        Assert.Equal(Path.GetFullPath(searched), found);
    }

    [Fact]
    public void Sans_pwsh_la_recherche_retourne_null()
    {
        Assert.Null(PowerShellExecutables.FindPowerShell7(
            null, [Path.Combine(_directory, "absent")], includeKnownInstallLocations: false));
    }

    [Fact]
    public void Un_chemin_vide_active_la_detection_automatique()
    {
        Assert.Null(PowerShellExecutables.NormalizeConfiguredPath("  "));
        Assert.Null(PowerShellExecutables.NormalizeConfiguredPath(null));
    }

    private string CreatePowerShell7(string directoryName)
    {
        var directory = Path.Combine(_directory, directoryName);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "pwsh.exe");
        File.WriteAllText(path, string.Empty);
        return path;
    }
}
