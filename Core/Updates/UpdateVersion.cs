using System.Globalization;

namespace EventGhostDotNet.Core.Updates;

/// <summary>
/// Version d'une publication, au format <c>major.minor.patch</c>.
/// Les étiquettes GitHub (<c>v1.2.3</c>, <c>1.2.3-beta</c>) sont acceptées.
/// </summary>
public readonly record struct UpdateVersion(int Major, int Minor, int Patch)
    : IComparable<UpdateVersion>
{
    public static bool TryParse(string? text, out UpdateVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
            value = value[1..];

        var suffix = value.IndexOfAny(['-', '+']);
        if (suffix >= 0)
            value = value[..suffix];

        var parts = value.Split('.');
        if (parts.Length is < 2 or > 3)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor))
            return false;

        var patch = 0;
        if (parts.Length == 3 &&
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out patch))
            return false;

        version = new UpdateVersion(major, minor, patch);
        return true;
    }

    public int CompareTo(UpdateVersion other)
    {
        var result = Major.CompareTo(other.Major);
        if (result != 0)
            return result;
        result = Minor.CompareTo(other.Minor);
        return result != 0 ? result : Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
