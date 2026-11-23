using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using EventGhostDotNet.PluginContracts;
using Markdig;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
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
                                           FileLoadException or WebView2RuntimeNotFoundException or
                                           BadImageFormatException or DllNotFoundException or
                                           TypeLoadException or COMException or InvalidOperationException)
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
        using var content = new WebView2();
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
                content.NavigateToString(RenderMessage(
                    "L'emplacement de la documentation de ce plugin est indisponible."));
                return;
            }
            try
            {
                var markdown = File.ReadAllText(document.Path);
                var baseUri = new Uri(Path.GetDirectoryName(document.Path) + Path.DirectorySeparatorChar)
                    .AbsoluteUri;
                content.NavigateToString(RenderMarkdown(markdown, baseUri));
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                content.NavigateToString(RenderMessage($"Documentation introuvable : {document.Path}"));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                var message = $"Impossible de lire la documentation : {document.Path}\n{exception.Message}";
                content.NavigateToString(RenderMessage(message));
                WriteLog(message, LogLevel.Warning);
            }
        }

        dialog.Loaded += async (_, _) =>
        {
            try
            {
                await content.EnsureCoreWebView2Async();
                if (!dialog.IsVisible)
                    return;
                if (selectPlugin && documents.Count == 1)
                    content.NavigateToString(RenderMessage("Aucun plugin n'est chargé."));
                else
                    list.SelectedIndex = selectPlugin ? 1 : 0;
            }
            catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or
                                               BadImageFormatException or DllNotFoundException or
                                               TypeLoadException or COMException or InvalidOperationException)
            {
                if (dialog.IsVisible)
                {
                    dialog.Close();
                    ReportDocumentationError(exception);
                }
            }
            catch (OperationCanceledException) when (!dialog.IsVisible)
            {
            }
        };
        dialog.ShowDialog();
    }

    private static string RenderMarkdown(string markdown, string baseUri)
    {
        var pipeline = new MarkdownPipelineBuilder()
            .DisableHtml()
            .UseAdvancedExtensions()
            .Build();
        var html = Markdown.ToHtml(markdown, pipeline);
        return $$"""
            <!doctype html>
            <html lang="fr">
            <head>
              <meta charset="utf-8">
              <base href="{{WebUtility.HtmlEncode(baseUri)}}">
              <style>
                body { font-family: "Segoe UI", sans-serif; font-size: 15px; line-height: 1.45; margin: 20px; color: #202020; }
                h1, h2, h3, h4 { margin-top: 1.2em; }
                code { font-family: Consolas, monospace; background: #f0f0f0; padding: 2px 4px; }
                pre { background: #f0f0f0; padding: 12px; overflow-x: auto; }
                pre code { padding: 0; background: transparent; }
                blockquote { border-left: 4px solid #bbb; margin-left: 0; padding-left: 12px; color: #555; }
                table { border-collapse: collapse; }
                th, td { border: 1px solid #bbb; padding: 6px 10px; }
                img { max-width: 100%; }
              </style>
            </head>
            <body>{{html}}</body>
            </html>
            """;
    }

    private static string RenderMessage(string message) =>
        $"<!doctype html><html lang=\"fr\"><body><p>{WebUtility.HtmlEncode(message).Replace("\n", "<br>")}</p></body></html>";
}
