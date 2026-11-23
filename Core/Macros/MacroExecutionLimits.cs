namespace EventGhostDotNet.Core.Macros;

/// <summary>
/// Règles d'exécution d'une macro, extraites du moteur WPF pour être vérifiables sans interface.
/// </summary>
public static class MacroExecutionLimits
{
    public const int MaximumCallDepth = 32;
    public const int MaximumActionExecutionsPerMacro = 10_000;
    public const int MaximumDelayMilliseconds = 600_000;

    public static void EnsureCallDepth(int callDepth)
    {
        if (callDepth >= MaximumCallDepth)
            throw new InvalidOperationException(
                $"La profondeur maximale d'appel de macro ({MaximumCallDepth}) est dépassée.");
    }

    public static bool CanExecuteAction(int executedActionCount) =>
        executedActionCount <= MaximumActionExecutionsPerMacro;

    public static int ParseDelay(string? value)
    {
        if (!int.TryParse(value, out var milliseconds) ||
            milliseconds is < 0 or > MaximumDelayMilliseconds)
            throw new InvalidOperationException($"Durée d'attente invalide : {value}");
        return milliseconds;
    }

    public static bool IsAllowedUrl(string? value, out Uri? uri) =>
        Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme is "http" or "https";
}
