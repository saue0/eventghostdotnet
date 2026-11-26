using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;

namespace EventGhostDotNet;

public partial class LogPreviewWindow : Window
{
    public const int MaximumLines = 200;

    public LogPreviewWindow()
    {
        InitializeComponent();
        PathText.Text = $"{DiagnosticLog.ApplicationLogPath} — {MaximumLines} dernières lignes maximum";
        RefreshPreview();
        // Le défilement demandé avant le rendu de la fenêtre n'est pas appliqué par WPF.
        Loaded += (_, _) => PreviewBox.ScrollToEnd();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshPreview();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(PreviewBox.Text);
        }
        catch (Exception exception) when (exception is ExternalException or InvalidOperationException)
        {
            MessageBox.Show(this, $"Impossible de copier le journal.\n\n{exception.Message}",
                "Aperçu du journal", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void RefreshPreview()
    {
        try
        {
            var lines = DiagnosticLog.ReadTail(MaximumLines);
            PreviewBox.Text = lines.Count == 0
                ? "Aucune entrée pour le moment."
                : string.Join(Environment.NewLine, lines);
            PreviewBox.CaretIndex = PreviewBox.Text.Length;
            PreviewBox.ScrollToEnd();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            PreviewBox.Text = $"Impossible de lire le journal :{Environment.NewLine}{exception.Message}";
        }
    }
}
