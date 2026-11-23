namespace EventGhostDotNet.Core.Logging;

/// <summary>
/// Couleur RVB d'un segment de journal, indépendante de WPF pour rester testable sans interface.
/// </summary>
public readonly record struct AnsiColor(byte Red, byte Green, byte Blue)
{
    public static AnsiColor FromRgb(byte red, byte green, byte blue) => new(red, green, blue);
}
