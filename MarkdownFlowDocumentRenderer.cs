using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Navigation;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using DocumentList = System.Windows.Documents.List;
using DocumentTable = System.Windows.Documents.Table;
using DocumentTableCell = System.Windows.Documents.TableCell;
using DocumentTableRow = System.Windows.Documents.TableRow;
using FontFamily = System.Windows.Media.FontFamily;
using ListItem = System.Windows.Documents.ListItem;
using MarkdigTable = Markdig.Extensions.Tables.Table;
using MarkdigTableCell = Markdig.Extensions.Tables.TableCell;
using MarkdigTableRow = Markdig.Extensions.Tables.TableRow;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace EventGhostDotNet;

/// <summary>
/// Rend la documentation Markdown en WPF, sans navigateur embarqué.
/// Le sous-ensemble couvert correspond aux README du projet : titres, paragraphes,
/// listes, citations, code, tableaux, liens, emphase et traits de séparation.
/// </summary>
internal static class MarkdownFlowDocumentRenderer
{
    private static readonly FontFamily BodyFont = new("Segoe UI");
    private static readonly FontFamily CodeFont = new("Consolas");
    private static readonly Brush BodyBrush = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
    private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
    private static readonly Brush CodeBackground = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
    private static readonly Brush RuleBrush = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB));

    public static FlowDocument Render(string markdown, string? baseDirectory)
    {
        var pipeline = new MarkdownPipelineBuilder()
            .DisableHtml()
            .UseAdvancedExtensions()
            .Build();
        var source = Markdown.Parse(markdown, pipeline);

        var document = CreateDocument();
        AddBlocks(document.Blocks, source, baseDirectory);
        return document;
    }

    public static FlowDocument RenderMessage(string message)
    {
        var document = CreateDocument();
        document.Blocks.Add(new Paragraph(new Run(message)));
        return document;
    }

    private static FlowDocument CreateDocument() => new()
    {
        FontFamily = BodyFont,
        FontSize = 15,
        Foreground = BodyBrush,
        PagePadding = new Thickness(20),
        ColumnWidth = double.PositiveInfinity
    };

    private static void AddBlocks(BlockCollection target, ContainerBlock source, string? baseDirectory)
    {
        foreach (var block in source)
            AddBlock(target, block, baseDirectory);
    }

    private static void AddBlock(BlockCollection target, Markdig.Syntax.Block block, string? baseDirectory)
    {
        switch (block)
        {
            case HeadingBlock heading:
            {
                var paragraph = new Paragraph
                {
                    FontWeight = FontWeights.SemiBold,
                    FontSize = heading.Level switch
                    {
                        1 => 26,
                        2 => 22,
                        3 => 19,
                        4 => 17,
                        _ => 15
                    },
                    Margin = new Thickness(0, heading.Level == 1 ? 0 : 10, 0, 8)
                };
                AddInlines(paragraph.Inlines, heading.Inline, baseDirectory);
                target.Add(paragraph);
                break;
            }
            case ParagraphBlock paragraphBlock:
            {
                var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
                AddInlines(paragraph.Inlines, paragraphBlock.Inline, baseDirectory);
                target.Add(paragraph);
                break;
            }
            case ListBlock listBlock:
                target.Add(CreateList(listBlock, baseDirectory));
                break;
            case QuoteBlock quote:
            {
                var section = new Section
                {
                    Margin = new Thickness(12, 0, 0, 8),
                    Foreground = MutedBrush
                };
                AddBlocks(section.Blocks, quote, baseDirectory);
                target.Add(section);
                break;
            }
            case FencedCodeBlock fenced:
                target.Add(CreateCode(fenced.Lines));
                break;
            case CodeBlock code:
                target.Add(CreateCode(code.Lines));
                break;
            case ThematicBreakBlock:
            {
                target.Add(new Paragraph
                {
                    BorderBrush = RuleBrush,
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    Margin = new Thickness(0, 8, 0, 8)
                });
                break;
            }
            case MarkdigTable table:
                target.Add(CreateTable(table, baseDirectory));
                break;
            case ContainerBlock container:
                AddBlocks(target, container, baseDirectory);
                break;
        }
    }

    private static DocumentList CreateList(ListBlock source, string? baseDirectory)
    {
        var list = new DocumentList
        {
            MarkerStyle = source.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(20, 0, 0, 0)
        };
        foreach (var item in source.OfType<ListItemBlock>())
        {
            var listItem = new ListItem();
            AddBlocks(listItem.Blocks, item, baseDirectory);
            list.ListItems.Add(listItem);
        }
        return list;
    }

    private static Paragraph CreateCode(StringLineGroup lines)
    {
        var text = string.Join("\n", lines.Lines.Select(line => line.ToString()));

        return new Paragraph(new Run(text))
        {
            FontFamily = CodeFont,
            FontSize = 13,
            Background = CodeBackground,
            Padding = new Thickness(10),
            Margin = new Thickness(0, 4, 0, 10)
        };
    }

    private static DocumentTable CreateTable(MarkdigTable source, string? baseDirectory)
    {
        var table = new DocumentTable { CellSpacing = 0, Margin = new Thickness(0, 4, 0, 10) };
        foreach (var _ in source.ColumnDefinitions)
            table.Columns.Add(new TableColumn());

        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        foreach (var row in source.OfType<MarkdigTableRow>())
        {
            var tableRow = new DocumentTableRow();
            foreach (var cell in row.OfType<MarkdigTableCell>())
            {
                var tableCell = new DocumentTableCell
                {
                    BorderBrush = RuleBrush,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(6, 3, 6, 3)
                };
                if (cell.ColumnSpan > 1)
                    tableCell.ColumnSpan = cell.ColumnSpan;
                AddBlocks(tableCell.Blocks, cell, baseDirectory);
                tableRow.Cells.Add(tableCell);
            }
            group.Rows.Add(tableRow);
        }
        return table;
    }

    private static void AddInlines(InlineCollection target, ContainerInline? source, string? baseDirectory)
    {
        if (source is null)
            return;

        foreach (var inline in source)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    target.Add(new Run(literal.Content.ToString()));
                    break;
                case EmphasisInline emphasis:
                {
                    var span = new Span();
                    if (emphasis.DelimiterChar is '~')
                        span.TextDecorations = TextDecorations.Strikethrough;
                    else if (emphasis.DelimiterCount > 1)
                        span.FontWeight = FontWeights.Bold;
                    else
                        span.FontStyle = FontStyles.Italic;
                    AddInlines(span.Inlines, emphasis, baseDirectory);
                    target.Add(span);
                    break;
                }
                case CodeInline code:
                    target.Add(new Run(code.Content) { FontFamily = CodeFont, Background = CodeBackground });
                    break;
                case LinkInline link when link.IsImage:
                    if (link.FirstChild is not null)
                    {
                        var alt = new Span { FontStyle = FontStyles.Italic };
                        AddInlines(alt.Inlines, link, baseDirectory);
                        target.Add(alt);
                    }
                    break;
                case LinkInline link:
                {
                    var uri = ResolveLink(link.Url, baseDirectory);
                    var span = new Span();
                    AddInlines(span.Inlines, link, baseDirectory);
                    if (uri is null)
                    {
                        target.Add(span);
                        break;
                    }

                    var hyperlink = new Hyperlink { NavigateUri = uri, ToolTip = uri.ToString() };
                    while (span.Inlines.FirstInline is { } child)
                    {
                        span.Inlines.Remove(child);
                        hyperlink.Inlines.Add(child);
                    }
                    hyperlink.RequestNavigate += OpenLink;
                    target.Add(hyperlink);
                    break;
                }
                case AutolinkInline autolink:
                {
                    var uri = ResolveLink(autolink.IsEmail ? $"mailto:{autolink.Url}" : autolink.Url, null);
                    if (uri is null)
                    {
                        target.Add(new Run(autolink.Url));
                        break;
                    }
                    var hyperlink = new Hyperlink(new Run(autolink.Url))
                    {
                        NavigateUri = uri,
                        ToolTip = uri.ToString()
                    };
                    hyperlink.RequestNavigate += OpenLink;
                    target.Add(hyperlink);
                    break;
                }
                case LineBreakInline lineBreak:
                    target.Add(lineBreak.IsHard ? new LineBreak() : new Run(" "));
                    break;
                case HtmlInline html:
                    target.Add(new Run(html.Tag));
                    break;
                case ContainerInline container:
                    AddInlines(target, container, baseDirectory);
                    break;
            }
        }
    }

    private static Uri? ResolveLink(string? url, string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(url) || url.StartsWith('#'))
            return null;

        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
            return absolute.Scheme is "http" or "https" or "mailto" ? absolute : null;

        if (baseDirectory is null)
            return null;

        var path = url.Split('#')[0];
        if (path.Length == 0)
            return null;
        try
        {
            return new Uri(Path.GetFullPath(Path.Combine(baseDirectory, Uri.UnescapeDataString(path))));
        }
        catch (Exception exception) when (exception is ArgumentException or UriFormatException or
                                           NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static void OpenLink(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            // Un lien invalide ne doit pas interrompre la consultation de la documentation.
        }
    }
}
