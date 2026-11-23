using System.Globalization;
using System.Text.Json;

namespace EventGhostDotNet.Core.Macros;

/// <summary>
/// Convertit le résultat d'une action en texte comparable par l'action <c>If</c>.
/// </summary>
public static class MacroValueFormatter
{
    public static string? Format(object? result) => result switch
    {
        null => null,
        string value => value,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
        JsonElement element => element.GetRawText(),
        IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
        _ => result.ToString()
    };
}
