using EventGhostDotNet.Core.Macros;
using Xunit;

namespace EventGhostDotNet.Core.Tests;

public sealed class MacroNavigationTests
{
    [Fact]
    public void Les_cibles_de_branche_sont_limitees_aux_sauts_vers_une_action()
    {
        var ifTarget = Guid.NewGuid();
        var elseTarget = Guid.NewGuid();
        var action = new MacroAction
        {
            Kind = MacroActionKind.IfJump,
            Target = ifTarget.ToString(),
            ElseTarget = elseTarget.ToString()
        };

        Assert.Equal([ifTarget, elseTarget], MacroNavigation.GetConditionalJumpTargetIds(action));

        action.ElseBranchMode = ConditionalJumpMode.Continue;
        Assert.Equal([ifTarget], MacroNavigation.GetConditionalJumpTargetIds(action));

        action.IfBranchMode = ConditionalJumpMode.GotoMacro;
        Assert.Empty(MacroNavigation.GetConditionalJumpTargetIds(action));
    }

    [Fact]
    public void Une_cible_de_branche_doit_etre_unique_et_presente()
    {
        var macro = new MacroDefinition { Name = "Branchement" };
        var target = new MacroAction { Kind = MacroActionKind.Log, Value = "cible" };
        macro.Actions.Add(target);

        Assert.Same(target, MacroNavigation.FindConditionalJumpTargetAction(macro, target.Id.ToString(), "If"));
        Assert.Throws<InvalidOperationException>(() =>
            MacroNavigation.FindConditionalJumpTargetAction(macro, Guid.NewGuid().ToString(), "Else"));
        Assert.Throws<InvalidOperationException>(() =>
            MacroNavigation.FindConditionalJumpTargetAction(macro, "pas-un-guid", "If"));
    }

    [Fact]
    public void Une_macro_cible_dupliquee_est_refusee()
    {
        var id = Guid.NewGuid();
        var macros = new[]
        {
            new MacroDefinition { Id = id, Name = "A" },
            new MacroDefinition { Id = id, Name = "B" }
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => MacroNavigation.FindSystemTargetMacro(macros, id.ToString()));
        Assert.Contains("dupliqué", exception.Message);
    }
}
