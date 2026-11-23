using System.Windows;
using EventGhostDotNet.PluginContracts;

namespace EventGhostDotNet.Plugins.Shared;

internal static class PluginHostWindows
{
    /// <summary>
    /// Le contrat de plugin ne référence plus WPF : les plugins WPF récupèrent ici
    /// la fenêtre native exposée par l'application pour leurs boîtes de dialogue.
    /// </summary>
    public static Window OwnerWindow(this IPluginHost host) =>
        host.UserInterface.NativeOwner as Window
        ?? throw new NotSupportedException("Ce plugin nécessite une fenêtre hôte WPF.");
}
