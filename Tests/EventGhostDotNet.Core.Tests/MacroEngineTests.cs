using EventGhostDotNet.Core.Macros;
using EventGhostDotNet.PluginContracts;
using Xunit;

namespace EventGhostDotNet.Core.Tests;

public sealed class MacroEngineTests
{
    [Fact]
    public async Task Execute_les_actions_dans_l_ordre_et_propage_le_resultat()
    {
        var host = new FakeHost();
        var macro = host.AddMacro("Ordre");
        macro.Actions.Add(Action(MacroActionKind.Log, "premier"));
        macro.Actions.Add(Action(MacroActionKind.Log, "{{$eg.result}} puis"));

        await new MacroEngine(host).ExecuteAsync(macro, CancellationToken.None);

        Assert.Equal(["premier", "premier puis"], host.ActionLogs);
    }

    [Fact]
    public async Task Stop_termine_la_sequence()
    {
        var host = new FakeHost();
        var macro = host.AddMacro("Stop");
        macro.Actions.Add(Action(MacroActionKind.Log, "avant"));
        macro.Actions.Add(Action(MacroActionKind.Stop));
        macro.Actions.Add(Action(MacroActionKind.Log, "après"));

        await new MacroEngine(host).ExecuteAsync(macro, CancellationToken.None);

        Assert.Equal(["avant"], host.ActionLogs);
    }

    [Fact]
    public async Task Une_action_desactivee_est_ignoree_et_efface_le_resultat()
    {
        var host = new FakeHost();
        var macro = host.AddMacro("Désactivée");
        macro.Actions.Add(Action(MacroActionKind.Log, "a"));
        macro.Actions.Add(new MacroAction { Kind = MacroActionKind.Log, Value = "b", IsEnabled = false });
        macro.Actions.Add(Action(MacroActionKind.Log, "{{$eg.result}}"));

        await new MacroEngine(host).ExecuteAsync(macro, CancellationToken.None);

        Assert.Equal(["a", ""], host.ActionLogs);
    }

    [Fact]
    public async Task If_vrai_laisse_passer_l_action_suivante()
    {
        var host = new FakeHost();
        var macro = host.AddMacro("If vrai");
        macro.Actions.Add(Action(MacroActionKind.Log, "abc"));
        macro.Actions.Add(new MacroAction
        {
            Kind = MacroActionKind.If, IfComparison = IfComparison.Equals, Value = "abc"
        });
        macro.Actions.Add(Action(MacroActionKind.Log, "suivante"));

        await new MacroEngine(host).ExecuteAsync(macro, CancellationToken.None);

        Assert.Equal(["abc", "suivante"], host.ActionLogs);
    }

    [Fact]
    public async Task If_faux_saute_l_action_suivante()
    {
        var host = new FakeHost();
        var macro = host.AddMacro("If faux");
        macro.Actions.Add(Action(MacroActionKind.Log, "abc"));
        macro.Actions.Add(new MacroAction
        {
            Kind = MacroActionKind.If, IfComparison = IfComparison.Equals, Value = "xyz"
        });
        macro.Actions.Add(Action(MacroActionKind.Log, "sautée"));
        macro.Actions.Add(Action(MacroActionKind.Log, "fin"));

        await new MacroEngine(host).ExecuteAsync(macro, CancellationToken.None);

        Assert.Equal(["abc", "fin"], host.ActionLogs);
    }

    [Fact]
    public async Task IfJump_vrai_saute_a_l_action_cible()
    {
        var host = new FakeHost();
        var macro = host.AddMacro("IfJump action");
        var target = Action(MacroActionKind.Log, "cible");
        macro.Actions.Add(new MacroAction
        {
            Kind = MacroActionKind.IfJump, IfLeft = "oui", IfComparison = IfComparison.Equals, Value = "oui",
            IfBranchMode = ConditionalJumpMode.GotoAction
        });
        macro.Actions.Add(Action(MacroActionKind.Log, "sautée"));
        macro.Actions.Add(target);
        macro.Actions.Add(Action(MacroActionKind.Log, "fin"));
        macro.Actions[0].Target = target.Id.ToString();

        await new MacroEngine(host).ExecuteAsync(macro, CancellationToken.None);

        Assert.Equal(["cible", "fin"], host.ActionLogs);
    }

    [Fact]
    public async Task IfJump_sinon_saute_a_la_macro_cible_et_arrete_la_courante()
    {
        var host = new FakeHost();
        var branch = host.AddMacro("Branche");
        branch.Actions.Add(Action(MacroActionKind.Log, "branche"));
        var macro = host.AddMacro("IfJump macro");
        macro.Actions.Add(new MacroAction
        {
            Kind = MacroActionKind.IfJump, IfLeft = "non", IfComparison = IfComparison.Equals, Value = "oui",
            ElseBranchMode = ConditionalJumpMode.GotoMacro, ElseTarget = branch.Id.ToString()
        });
        macro.Actions.Add(Action(MacroActionKind.Log, "après"));

        await new MacroEngine(host).ExecuteAsync(macro, CancellationToken.None);

        Assert.Equal(["branche"], host.ActionLogs);
    }

    [Fact]
    public async Task Jump_appelle_la_macro_puis_reprend()
    {
        var host = new FakeHost();
        var target = host.AddMacro("Cible");
        target.Actions.Add(Action(MacroActionKind.Log, "cible"));
        var macro = host.AddMacro("Appel");
        macro.Actions.Add(new MacroAction
        {
            Kind = MacroActionKind.Jump, JumpMode = JumpMode.CallAndResume, Target = target.Id.ToString()
        });
        macro.Actions.Add(Action(MacroActionKind.Log, "reprise"));

        await new MacroEngine(host).ExecuteAsync(macro, CancellationToken.None);

        Assert.Equal(["cible", "reprise"], host.ActionLogs);
    }

    [Fact]
    public async Task Jump_appelle_la_macro_puis_arrete()
    {
        var host = new FakeHost();
        var target = host.AddMacro("Cible");
        target.Actions.Add(Action(MacroActionKind.Log, "cible"));
        var macro = host.AddMacro("Appel et arrêt");
        macro.Actions.Add(new MacroAction
        {
            Kind = MacroActionKind.Jump, JumpMode = JumpMode.CallAndStop, Target = target.Id.ToString()
        });
        macro.Actions.Add(Action(MacroActionKind.Log, "après"));

        await new MacroEngine(host).ExecuteAsync(macro, CancellationToken.None);

        Assert.Equal(["cible"], host.ActionLogs);
    }

    [Fact]
    public async Task Jump_goto_saute_dans_la_sequence()
    {
        var host = new FakeHost();
        var macro = host.AddMacro("Goto");
        var target = Action(MacroActionKind.Log, "cible");
        macro.Actions.Add(new MacroAction { Kind = MacroActionKind.Jump, JumpMode = JumpMode.GotoAction });
        macro.Actions.Add(Action(MacroActionKind.Log, "sautée"));
        macro.Actions.Add(target);
        macro.Actions.Add(Action(MacroActionKind.Log, "fin"));
        macro.Actions[0].Value = target.Id.ToString();

        await new MacroEngine(host).ExecuteAsync(macro, CancellationToken.None);

        Assert.Equal(["cible", "fin"], host.ActionLogs);
    }

    [Fact]
    public async Task Seule_l_action_demandee_est_executee()
    {
        var host = new FakeHost();
        var macro = host.AddMacro("Action seule");
        macro.Actions.Add(Action(MacroActionKind.Log, "a"));
        var selected = Action(MacroActionKind.Log, "sélection");
        macro.Actions.Add(selected);
        macro.Actions.Add(Action(MacroActionKind.Log, "c"));

        await new MacroEngine(host).ExecuteAsync(macro, CancellationToken.None, onlyActionId: selected.Id);

        Assert.Equal(
            ["Exécution de l'action « Journaliser : sélection ».", "sélection",
                "Action terminée : « Journaliser : sélection »."],
            host.ActionLogs);
    }

    [Fact]
    public async Task La_profondeur_d_appel_est_limitee()
    {
        var host = new FakeHost();
        var macro = host.AddMacro("Profondeur");

        await Assert.ThrowsAsync<InvalidOperationException>(() => new MacroEngine(host).ExecuteAsync(
            macro, CancellationToken.None, callDepth: MacroExecutionLimits.MaximumCallDepth));
    }

    [Fact]
    public async Task La_limite_d_executions_d_actions_arrete_les_boucles()
    {
        var host = new FakeHost();
        var macro = host.AddMacro("Boucle");
        var jump = new MacroAction { Kind = MacroActionKind.Jump, JumpMode = JumpMode.GotoAction };
        macro.Actions.Add(jump);
        jump.Value = jump.Id.ToString();

        await new MacroEngine(host).ExecuteAsync(macro, CancellationToken.None);

        Assert.Contains(host.Warnings, message =>
            message.Contains($"Limite de {MacroExecutionLimits.MaximumActionExecutionsPerMacro}"));
    }

    [Fact]
    public async Task Un_jeton_annule_arrete_la_sequence()
    {
        var host = new FakeHost();
        var macro = host.AddMacro("Annulation");
        using var cancellation = new CancellationTokenSource();
        host.ActionLogged = message =>
        {
            if (message == "a")
                cancellation.Cancel();
        };
        macro.Actions.Add(Action(MacroActionKind.Log, "a"));
        macro.Actions.Add(Action(MacroActionKind.Log, "b"));

        await new MacroEngine(host).ExecuteAsync(macro, cancellation.Token);

        Assert.Equal(["a"], host.ActionLogs);
        Assert.DoesNotContain(host.Logs, log => log.Message == "Macro terminée : « Annulation ».");
    }

    [Fact]
    public async Task Une_erreur_d_action_est_journalisee_et_la_sequence_continue()
    {
        var host = new FakeHost();
        var macro = host.AddMacro("Erreur");
        macro.Actions.Add(Action(MacroActionKind.Log, "a"));
        macro.Actions.Add(Action(MacroActionKind.OpenUrl, "ftp://example.com"));
        macro.Actions.Add(Action(MacroActionKind.Log, "b"));

        await new MacroEngine(host).ExecuteAsync(macro, CancellationToken.None);

        Assert.Equal(["a", "b"], host.ActionLogs);
        Assert.Empty(host.Urls);
        Assert.Contains(host.Logs, log => log.Level == LogLevel.Error &&
                                          log.Message.Contains("URL non autorisée"));
    }

    [Fact]
    public async Task Les_dialogues_et_notifications_passent_par_l_hote()
    {
        var host = new FakeHost();
        host.Answers.Enqueue(true);
        var macro = host.AddMacro("Dialogue");
        macro.Actions.Add(Action(MacroActionKind.ShowMessage, "message"));
        macro.Actions.Add(Action(MacroActionKind.AskYesNo, "question ?"));
        macro.Actions.Add(Action(MacroActionKind.Log, "{{$eg.result}}"));
        macro.Actions.Add(new MacroAction
        {
            Kind = MacroActionKind.Notify, Value = "texte", Target = "titre", NotificationKey = "clé"
        });
        macro.Actions.Add(Action(MacroActionKind.Beep));

        await new MacroEngine(host).ExecuteAsync(macro, CancellationToken.None);

        Assert.Equal([("Dialogue", "message")], host.Messages);
        Assert.Equal([("titre", "texte", "clé")], host.Notifications);
        Assert.Equal(["Oui"], host.ActionLogs);
        Assert.Equal(1, host.Beeps);
    }

    [Fact]
    public async Task PowerShell_et_plugin_sont_executes_par_l_hote()
    {
        var host = new FakeHost();
        var macro = host.AddMacro("Services");
        macro.Actions.Add(Action(MacroActionKind.PowerShell, "Get-Date"));
        macro.Actions.Add(new MacroAction
        {
            Kind = MacroActionKind.Plugin, PluginId = "demo", ActionId = "run"
        });

        await new MacroEngine(host).ExecuteAsync(macro, CancellationToken.None);

        Assert.Equal(1, host.PowerShellRuns);
        Assert.Equal(1, host.PluginRuns);
    }

    [Fact]
    public async Task Une_action_systeme_active_la_cible_et_met_a_jour_la_reference()
    {
        var host = new FakeHost();
        var target = host.AddMacro("Cible");
        target.IsEnabled = false;
        var macro = host.AddMacro("Activation");
        macro.Actions.Add(new MacroAction
        {
            Kind = MacroActionKind.EnableMacro, Target = target.Id.ToString()
        });
        macro.Actions.Add(Action(MacroActionKind.Log, "suite"));

        await new MacroEngine(host).ExecuteAsync(macro, CancellationToken.None);

        Assert.True(target.IsEnabled);
        var result = Assert.Single(host.SystemResults);
        Assert.False(result.PreviousState);
        Assert.True(result.CurrentState);
        Assert.Equal(["Macro « Cible » : activé.", "suite"], host.ActionLogs);
    }

    [Fact]
    public async Task Desactiver_un_demarrage_de_plugin_redemarre_la_session_et_arrete_la_macro()
    {
        var host = new FakeHost();
        var startup = new PluginStartupConfiguration { InstanceId = "instance-1", DisplayName = "Demo" };
        host.StartupConfigurationList.Add(startup);
        var macro = host.AddMacro("Plugin");
        macro.Actions.Add(new MacroAction
        {
            Kind = MacroActionKind.DeactivateItem,
            ActivationTarget = ActivationTargetKind.PluginStartup,
            Target = startup.InstanceId
        });
        macro.Actions.Add(Action(MacroActionKind.Log, "après"));

        await new MacroEngine(host).ExecuteAsync(macro, CancellationToken.None);

        Assert.False(startup.IsEnabled);
        Assert.Equal(1, host.StartupRestarts);
        Assert.DoesNotContain("après", host.ActionLogs);
    }

    [Fact]
    public async Task Le_journal_indente_les_actions_sous_la_macro()
    {
        var host = new FakeHost();
        var macro = host.AddMacro("Indentation");
        macro.Actions.Add(Action(MacroActionKind.Log, "a"));

        await new MacroEngine(host).ExecuteAsync(macro, CancellationToken.None, logIndentLevel: 2);

        Assert.Contains(host.Logs, log => log.Category == MacroLogCategory.Macro &&
                                          log.Message.StartsWith("Exécution de la macro") && log.IndentLevel == 2);
        Assert.Contains(host.Logs, log => log.Category == MacroLogCategory.Action &&
                                          log.Message == "a" && log.IndentLevel == 3);
    }

    private static MacroAction Action(MacroActionKind kind, string value = "") =>
        new() { Kind = kind, Value = value };

    private sealed class FakeHost : IMacroExecutionHost
    {
        public List<MacroGroup> GroupList { get; } = [];
        public List<MacroDefinition> MacroList { get; } = [];
        public List<MacroAction> StartupList { get; } = [];
        public List<PluginStartupConfiguration> StartupConfigurationList { get; } = [];

        public IReadOnlyList<MacroGroup> Groups => GroupList;
        public IReadOnlyList<MacroDefinition> Macros => MacroList;
        public IReadOnlyList<MacroAction> StartupActions => StartupList;
        public IReadOnlyList<PluginStartupConfiguration> PluginStartups => StartupConfigurationList;

        public List<LogEntry> Logs { get; } = [];
        public List<string> ActionLogs => Logs
            .Where(log => log.Category == MacroLogCategory.Action &&
                          log.Level == LogLevel.Information)
            .Select(log => log.Message)
            .ToList();
        public List<string> Warnings => Logs
            .Where(log => log.Level == LogLevel.Warning).Select(log => log.Message).ToList();
        public Queue<bool> Answers { get; } = new();
        public List<(string Title, string Message)> Messages { get; } = [];
        public List<(string Title, string Message, string? Key)> Notifications { get; } = [];
        public List<Uri> Urls { get; } = [];
        public List<SystemActionResult> SystemResults { get; } = [];
        public Action<string>? ActionLogged { get; set; }
        public int Beeps { get; private set; }
        public int PowerShellRuns { get; private set; }
        public int PluginRuns { get; private set; }
        public int StartupRestarts { get; private set; }

        public MacroDefinition AddMacro(string name)
        {
            var macro = new MacroDefinition { Name = name };
            MacroList.Add(macro);
            return macro;
        }

        public void Log(string message, LogLevel level, MacroLogCategory category, int indentLevel)
        {
            Logs.Add(new LogEntry(message, level, category, indentLevel));
            if (category == MacroLogCategory.Action)
                ActionLogged?.Invoke(message);
        }

        public string ExpandTemplate(string template, object? previousResult, TriggerEvent? triggerEvent) =>
            template
                .Replace("{{$eg.result}}", MacroValueFormatter.Format(previousResult) ?? string.Empty,
                    StringComparison.Ordinal)
                .Replace("{{$eg.event.name}}", triggerEvent?.Name ?? string.Empty, StringComparison.Ordinal);

        public MacroExecutionContext CreateContext(
            CancellationToken sessionToken, object? initialResult, TriggerEvent? triggerEvent, int? logIndentLevel) =>
            new(sessionToken, initialResult, triggerEvent, logIndentLevel,
                (message, level, indent) => Log(message, level, MacroLogCategory.Action, indent ?? 1),
                (_, _, _) => { });

        public void ShowMessage(string title, string message) => Messages.Add((title, message));

        public bool AskYesNo(string title, string message) => Answers.Count > 0 && Answers.Dequeue();

        public void ShowNotification(string title, string message, string? key) =>
            Notifications.Add((title, message, key));

        public void Beep() => Beeps++;

        public void OpenUrl(Uri uri) => Urls.Add(uri);

        public Task RunPowerShellAsync(
            MacroAction action, MacroExecutionContext context, CancellationToken cancellationToken)
        {
            PowerShellRuns++;
            context.Result = "powershell";
            return Task.CompletedTask;
        }

        public Task RunPluginActionAsync(
            MacroAction action, MacroExecutionContext context, CancellationToken cancellationToken)
        {
            PluginRuns++;
            return Task.CompletedTask;
        }

        public void ApplySystemActionResult(SystemActionResult result) => SystemResults.Add(result);

        public void OnPluginStartupStateChanged() => StartupRestarts++;
    }

    private sealed record LogEntry(string Message, LogLevel Level, MacroLogCategory Category, int IndentLevel);
}
