using EventGhostDotNet.PluginContracts;
using Xunit;

namespace EventGhostDotNet.Core.Tests;

public sealed class PluginContractsTests
{
    [Theory]
    [InlineData("PresentationFramework")]
    [InlineData("PresentationCore")]
    [InlineData("WindowsBase")]
    [InlineData("System.Windows.Forms")]
    public void Le_contrat_ne_reference_pas_WPF(string assemblyName)
    {
        var references = typeof(IPluginHost).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();

        Assert.DoesNotContain(assemblyName, references);
    }

    [Fact]
    public void Un_hote_de_test_peut_implementer_le_contrat_sans_WPF()
    {
        IPluginHost host = new HeadlessHost();

        Assert.Null(host.UserInterface.NativeOwner);
        Assert.False(host.UserInterface.Confirm("Continuer ?", "Test"));
    }

    private sealed class HeadlessHost : IPluginHost
    {
        public IPluginUserInterface UserInterface { get; } = new HeadlessUserInterface();

        public void Log(string message)
        {
        }

        public void RaiseEvent(string name, string? payload)
        {
        }

        public IReadOnlyList<PluginActionData> GetConfiguredActions(string pluginId) => [];
    }

    private sealed class HeadlessUserInterface : IPluginUserInterface
    {
        public object? NativeOwner => null;

        public bool? ShowDialog(object dialog) => null;

        public void ShowMessage(string message, string title)
        {
        }

        public bool Confirm(string message, string title) => false;
    }
}
