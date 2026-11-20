using System.Text.Json;
using System.Windows.Threading;

namespace EventGhostDotNet;

public partial class MainWindow
{
    private readonly Dictionary<string, JsonElement> _scriptVariables = new(StringComparer.Ordinal);
    private readonly object _scriptVariablesLock = new();

    private async Task<object?> ExecuteScriptRequestAsync(ActionContext context, JsonElement request)
    {
        var operation = request.GetProperty("Operation").GetString();
        var name = request.GetProperty("Name").GetString();
        if (string.IsNullOrWhiteSpace(operation) || string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Le nom de l'opération et celui de la variable ou notification sont obligatoires.");

        if (operation == "Notify")
        {
            var message = request.GetProperty("Value").GetString();
            if (string.IsNullOrWhiteSpace(message))
                throw new InvalidOperationException("Le texte de la notification est obligatoire.");

            string? key = null;
            if (request.TryGetProperty("Key", out var keyElement) && keyElement.ValueKind != JsonValueKind.Null)
            {
                if (keyElement.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(key = keyElement.GetString()))
                    throw new InvalidOperationException("La clé de notification doit être une chaîne non vide.");
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (System.Windows.Application.Current is not App app)
                    throw new InvalidOperationException("L'application de notification n'est pas disponible.");
                app.ShowNotification(name, message, key);
            }, DispatcherPriority.Normal, context.CancellationToken);
            return null;
        }

        lock (_scriptVariablesLock)
        {
            switch (operation)
            {
                case "Get":
                    return _scriptVariables.TryGetValue(name, out var value) ? value : null;
                case "Has":
                    return _scriptVariables.ContainsKey(name);
                case "Set":
                    _scriptVariables[name] = request.GetProperty("Value").Clone();
                    return null;
                case "Exchange":
                    var previous = _scriptVariables.TryGetValue(name, out var existing) ? existing : (JsonElement?)null;
                    _scriptVariables[name] = request.GetProperty("Value").Clone();
                    return previous;
                case "Remove":
                    return _scriptVariables.Remove(name);
                default:
                    throw new InvalidOperationException($"Opération de variable inconnue : {operation}");
            }
        }
    }
}
