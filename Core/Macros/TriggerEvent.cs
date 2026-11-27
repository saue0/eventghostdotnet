namespace EventGhostDotNet.Core.Macros;

/// <summary>Événement qui a déclenché l'exécution d'une macro.</summary>
public sealed record TriggerEvent(string Name, string? Payload, string? SourceId);
