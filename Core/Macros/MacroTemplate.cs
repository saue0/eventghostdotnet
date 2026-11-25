using System.Text.RegularExpressions;

namespace EventGhostDotNet.Core.Macros;

/// <summary>
/// Remplace les jetons <c>{{$eg.…}}</c> d'un texte d'action par les valeurs du contexte.
/// </summary>
public static partial class MacroTemplate
{
    /// <summary>
    /// Développe les jetons reconnus par <paramref name="resolve"/>. Un résolveur qui retourne
    /// <c>null</c> laisse le jeton intact, ce qui signale une variable inconnue ; une valeur
    /// absente doit donc être retournée sous forme de chaîne vide.
    /// </summary>
    public static string Expand(string template, Func<string, string?> resolve)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(resolve);
        if (!template.Contains("{{", StringComparison.Ordinal))
            return template;

        return TokenPattern().Replace(template, match =>
            resolve(match.Groups["path"].Value) ?? match.Value);
    }

    [GeneratedRegex(@"\{\{\s*\$eg\.(?<path>[^{}]+?)\s*\}\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
