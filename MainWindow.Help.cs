using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using EventGhostDotNet.PluginContracts;
using Button = System.Windows.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;

namespace EventGhostDotNet;

public partial class MainWindow
{
    private sealed record HelpDocument(string Label, string? Path);

    private void ShowDocumentation_Click(object sender, RoutedEventArgs e) =>
        OpenDocumentation(false);

    private void ShowPluginDocumentation_Click(object sender, RoutedEventArgs e) =>
        OpenDocumentation(true);

    private void OpenDocumentation(bool selectPlugin)
    {
        try
        {
            ShowDocumentation(selectPlugin);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           InvalidOperationException)
        {
            ReportDocumentationError(exception);
        }
    }

    private void ReportDocumentationError(Exception exception)
    {
        var message = $"Impossible d'ouvrir la documentation : {exception.Message}";
        WriteLog(message, LogLevel.Warning);
        System.Windows.MessageBox.Show(this, message, "Documentation", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ShowDocumentation(bool selectPlugin)
    {
        var documents = new List<HelpDocument>
        {
            new("Application", Path.Combine(GetApplicationDirectory(), "README.md")),
            new("Système", Path.Combine(GetApplicationDirectory(), "Items", "README.md"))
        };
        documents.AddRange(_pluginRuntime.Plugins
            .OrderBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(plugin => plugin.Id, StringComparer.OrdinalIgnoreCase)
            .Select(plugin =>
            {
                var directory = Path.GetDirectoryName(plugin.GetType().Assembly.Location);
                return new HelpDocument($"{plugin.Name} ({plugin.Id})",
                    string.IsNullOrEmpty(directory) ? null : Path.Combine(directory, "README.md"));
            }));

        var dialog = new Window
        {
            Title = "Documentation",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 950,
            Height = 650,
            MinWidth = 600,
            MinHeight = 400
        };
        var layout = new DockPanel { Margin = new Thickness(10) };
        var close = new Button
        {
            Content = "Fermer",
            IsCancel = true,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        DockPanel.SetDock(close, Dock.Bottom);
        layout.Children.Add(close);

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var list = new ListBox
        {
            ItemsSource = documents,
            DisplayMemberPath = nameof(HelpDocument.Label),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var content = new FlowDocumentScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsToolBarVisible = false,
            Document = MarkdownFlowDocumentRenderer.RenderMessage("Sélectionnez un document.")
        };
        content.SetResourceReference(FlowDocumentScrollViewer.BackgroundProperty, "WindowBackgroundBrush");
        Grid.SetColumn(content, 1);
        columns.Children.Add(list);
        columns.Children.Add(content);
        layout.Children.Add(columns);
        dialog.Content = layout;

        list.SelectionChanged += (_, _) => LoadDocumentation(list.SelectedItem as HelpDocument);

        void LoadDocumentation(HelpDocument? document)
        {
            if (document is null)
                return;
            if (document.Path is null)
            {
                content.Document = MarkdownFlowDocumentRenderer.RenderMessage(
                    "L'emplacement de la documentation de ce plugin est indisponible.");
                return;
            }
            try
            {
                var markdown = File.ReadAllText(document.Path);
                content.Document = MarkdownFlowDocumentRenderer.Render(
                    markdown, Path.GetDirectoryName(document.Path));
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                content.Document = MarkdownFlowDocumentRenderer.RenderMessage(
                    $"Documentation introuvable : {document.Path}");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                var message = $"Impossible de lire la documentation : {document.Path}\n{exception.Message}";
                content.Document = MarkdownFlowDocumentRenderer.RenderMessage(message);
                WriteLog(message, LogLevel.Warning);
            }
            catch (Exception exception)
            {
                var message = $"Impossible de rendre la documentation : {document.Path}\n{exception.Message}";
                content.Document = MarkdownFlowDocumentRenderer.RenderMessage(message);
                WriteLog(message, LogLevel.Warning);
            }
        }

        dialog.Loaded += (_, _) =>
        {
            if (selectPlugin && documents.Count == 1)
            {
                content.Document = MarkdownFlowDocumentRenderer.RenderMessage("Aucun plugin n'est chargé.");
                return;
            }
            list.SelectedIndex = selectPlugin ? 1 : 0;
        };
        dialog.ShowDialog();
    }
}
