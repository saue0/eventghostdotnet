namespace EventGhostDotNet.Core.Macros;

public enum IfOperator
{
    Equals,
    Contains,
    NotEquals
}

public static class MacroConditionEvaluator
{
    public static bool Evaluate(string? previousResult, IfOperator comparison, string expected)
    {
        if (previousResult is null)
            return false;

        expected ??= string.Empty;
        return comparison switch
        {
            IfOperator.Equals => string.Equals(previousResult, expected, StringComparison.OrdinalIgnoreCase),
            IfOperator.Contains => previousResult.Contains(expected, StringComparison.OrdinalIgnoreCase),
            IfOperator.NotEquals => !string.Equals(previousResult, expected, StringComparison.OrdinalIgnoreCase),
            _ => throw new InvalidOperationException("La comparaison de l'action If est invalide.")
        };
    }
}
