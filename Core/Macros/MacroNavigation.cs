namespace EventGhostDotNet.Core.Macros;

/// <summary>Résolution des cibles des actions Jump et If / Jump.</summary>
public static class MacroNavigation
{
    public static MacroDefinition FindSystemTargetMacro(IEnumerable<MacroDefinition> macros, string? target)
    {
        if (!Guid.TryParse(target, out var id) || id == Guid.Empty)
            throw new InvalidOperationException("Identifiant de macro cible invalide.");
        var matches = macros.Where(macro => macro.Id == id).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] :
            throw new InvalidOperationException("La macro cible est introuvable ou son identifiant est dupliqué.");
    }

    public static MacroDefinition FindSystemTargetMacro(IEnumerable<MacroDefinition> macros, MacroAction action) =>
        FindSystemTargetMacro(macros, action.Target);

    public static MacroAction FindJumpTargetAction(MacroDefinition macro, MacroAction action)
    {
        if (!Guid.TryParse(action.Value, out var id) || id == Guid.Empty)
            throw new InvalidOperationException("Identifiant d'action Jump invalide.");
        var matches = macro.Actions.Where(item => item.Id == id).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] :
            throw new InvalidOperationException($"L'action cible de Jump dans « {macro.Name} » est introuvable ou son identifiant est dupliqué.");
    }

    public static MacroAction FindConditionalJumpTargetAction(
        MacroDefinition macro, string? target, string branch)
    {
        if (!Guid.TryParse(target, out var id) || id == Guid.Empty)
            throw new InvalidOperationException($"Identifiant de la branche {branch} de If / Jump invalide.");
        var matches = macro.Actions.Where(item => item.Id == id).Take(2).ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"L'action cible de la branche {branch} de If / Jump dans « {macro.Name} » est introuvable ou son identifiant est dupliqué.");
    }

    public static Guid[] GetConditionalJumpTargetIds(MacroAction action)
    {
        if (action.Kind != MacroActionKind.IfJump)
            return [];
        var targets = new List<Guid>();
        if ((action.IfBranchMode ?? ConditionalJumpMode.GotoAction) == ConditionalJumpMode.GotoAction &&
            Guid.TryParse(action.Target, out var ifTarget))
            targets.Add(ifTarget);
        if ((action.ElseBranchMode ?? ConditionalJumpMode.GotoAction) == ConditionalJumpMode.GotoAction &&
            Guid.TryParse(action.ElseTarget, out var elseTarget))
            targets.Add(elseTarget);
        return targets.ToArray();
    }
}
