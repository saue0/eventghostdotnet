using System.Security.Cryptography;

namespace EventGhostDotNet.Core.Plugins;

/// <summary>Calcul et comparaison de l'empreinte SHA-256 des DLL de plugins.</summary>
public static class PluginIntegrity
{
    public static string ComputeSha256(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public static bool Matches(string path, string expectedSha256) =>
        string.Equals(ComputeSha256(path), expectedSha256, StringComparison.OrdinalIgnoreCase);
}
