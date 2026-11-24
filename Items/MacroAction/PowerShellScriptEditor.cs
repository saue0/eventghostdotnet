using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Indentation;
using ICSharpCode.AvalonEdit.Rendering;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;

namespace EventGhostDotNet;

/// <summary>
/// Éditeur de scripts PowerShell basé sur AvalonEdit, colorisé par la grammaire TextMate
/// PowerShell (TextMateSharp) avec les couleurs du thème clair de Visual Studio Code.
/// </summary>
public sealed class PowerShellScriptEditor : Border
{
    private static readonly IGrammar? Grammar = CreateGrammar();

    private static IGrammar? CreateGrammar()
    {
        try
        {
            var options = new RegistryOptions(ThemeName.LightPlus);
            var registry = new Registry(options);
            var scope = options.GetScopeByExtension(".ps1");
            return scope is null ? null : registry.LoadGrammar(scope);
        }
        catch (Exception)
        {
            // Sans grammaire, l'éditeur reste utilisable en texte brut.
            return null;
        }
    }

    private readonly TextEditor _editor;
    private readonly TextMateColorizer? _colorizer;

    public PowerShellScriptEditor()
    {
        Background = Brushes.White;
        BorderBrush = new SolidColorBrush(Color.FromRgb(0xD7, 0xDD, 0xE6));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(2);
        MinWidth = 640;
        MinHeight = 300;

        _editor = new TextEditor
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            ShowLineNumbers = true,
            LineNumbersForeground = new SolidColorBrush(Color.FromRgb(0x98, 0xA2, 0xB3)),
            WordWrap = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 2, 2, 2),
            Options =
            {
                ConvertTabsToSpaces = true,
                IndentationSize = 4,
                EnableHyperlinks = false,
                HighlightCurrentLine = false
            }
        };
        _editor.TextArea.IndentationStrategy = new DefaultIndentationStrategy();

        if (Grammar is { } grammar)
        {
            _colorizer = new TextMateColorizer(_editor.Document, grammar);
            _editor.TextArea.TextView.LineTransformers.Add(_colorizer);
            _editor.Document.Changed += (_, change) =>
            {
                _colorizer.InvalidateFrom(_editor.Document.GetLineByOffset(change.Offset).LineNumber);
                _editor.TextArea.TextView.Redraw();
            };
        }

        _editor.TextArea.GotKeyboardFocus += (_, _) =>
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
        _editor.TextArea.LostKeyboardFocus += (_, _) =>
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD7, 0xDD, 0xE6));

        Child = _editor;
    }

    public string Text
    {
        get => _editor.Text;
        set => _editor.Text = value;
    }

    public void FocusEditor() => _editor.Focus();

    /// <summary>
    /// Colorise chaque ligne à partir des jetons produits par la grammaire TextMate.
    /// Les états de fin de ligne sont mis en cache pour ne retokeniser que le nécessaire.
    /// </summary>
    private sealed class TextMateColorizer : DocumentColorizingTransformer
    {
        private static readonly Brush CommentBrush = Frozen(0x00, 0x80, 0x00);
        private static readonly Brush StringBrush = Frozen(0xA3, 0x15, 0x15);
        private static readonly Brush NumberBrush = Frozen(0x09, 0x86, 0x58);
        private static readonly Brush KeywordBrush = Frozen(0x00, 0x00, 0xFF);
        private static readonly Brush VariableBrush = Frozen(0x00, 0x10, 0x80);
        private static readonly Brush FunctionBrush = Frozen(0x79, 0x5E, 0x26);
        private static readonly Brush TypeBrush = Frozen(0x26, 0x7F, 0x99);
        private static readonly Brush InvalidBrush = Frozen(0xCD, 0x31, 0x31);

        private readonly TextDocument _document;
        private readonly IGrammar _grammar;
        private readonly Dictionary<int, LineResult> _lines = new();

        public TextMateColorizer(TextDocument document, IGrammar grammar)
        {
            _document = document;
            _grammar = grammar;
        }

        public void InvalidateFrom(int lineNumber)
        {
            foreach (var cached in _lines.Keys.Where(line => line >= lineNumber).ToArray())
                _lines.Remove(cached);
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            var text = _document.GetText(line);
            if (text.Length == 0)
                return;

            foreach (var token in GetLine(line.LineNumber, text).Tokens)
            {
                var (brush, italic) = ResolveStyle(token.Scopes);
                if (brush is null && !italic)
                    continue;

                var start = line.Offset + Math.Min(token.Start, text.Length);
                var end = line.Offset + Math.Min(Math.Max(token.End, token.Start), text.Length);
                if (end <= start)
                    continue;

                ChangeLinePart(start, end, element =>
                {
                    if (brush is not null)
                        element.TextRunProperties.SetForegroundBrush(brush);
                    if (italic)
                        element.TextRunProperties.SetTypeface(new Typeface(
                            element.TextRunProperties.Typeface.FontFamily, FontStyles.Italic,
                            element.TextRunProperties.Typeface.Weight, element.TextRunProperties.Typeface.Stretch));
                });
            }
        }

        private LineResult GetLine(int lineNumber, string text)
        {
            if (_lines.TryGetValue(lineNumber, out var cached))
                return cached;

            IStateStack? state = null;
            var firstLine = 1;
            for (var candidate = lineNumber - 1; candidate >= 1; candidate--)
            {
                if (!_lines.TryGetValue(candidate, out var previous))
                    continue;
                state = previous.State;
                firstLine = candidate + 1;
                break;
            }

            cached = null!;
            for (var current = firstLine; current <= lineNumber; current++)
            {
                var currentText = current == lineNumber ? text : _document.GetText(_document.GetLineByNumber(current));
                var result = _grammar.TokenizeLine(currentText, state, TimeSpan.FromSeconds(1));
                cached = new LineResult(result.RuleStack, BuildTokens(result));
                _lines[current] = cached;
                state = result.RuleStack;
            }

            return cached;
        }

        private static List<TokenSpan> BuildTokens(ITokenizeLineResult result)
        {
            var tokens = new List<TokenSpan>();
            foreach (var token in result.Tokens)
                tokens.Add(new TokenSpan(token.StartIndex, token.EndIndex, token.Scopes.ToArray()));
            return tokens;
        }

        private static (Brush? Brush, bool Italic) ResolveStyle(IReadOnlyList<string> scopes)
        {
            // Le dernier scope est le plus spécifique : on le teste en premier.
            for (var index = scopes.Count - 1; index >= 0; index--)
            {
                var scope = scopes[index];
                if (scope.Contains("comment", StringComparison.Ordinal))
                    return (CommentBrush, true);
                if (scope.Contains("invalid", StringComparison.Ordinal))
                    return (InvalidBrush, false);
                if (scope.Contains("keyword.operator", StringComparison.Ordinal))
                    return (null, false);
                if (scope.Contains("string", StringComparison.Ordinal))
                    return (StringBrush, false);
                if (scope.Contains("constant.numeric", StringComparison.Ordinal))
                    return (NumberBrush, false);
                if (scope.Contains("support.function", StringComparison.Ordinal) ||
                    scope.Contains("entity.name.function", StringComparison.Ordinal))
                    return (FunctionBrush, false);
                if (scope.Contains("entity.name.type", StringComparison.Ordinal) ||
                    scope.Contains("support.class", StringComparison.Ordinal) ||
                    scope.Contains("support.type", StringComparison.Ordinal))
                    return (TypeBrush, false);
                if (scope.Contains("variable", StringComparison.Ordinal))
                    return (VariableBrush, false);
                if (scope.Contains("keyword", StringComparison.Ordinal) ||
                    scope.Contains("storage", StringComparison.Ordinal) ||
                    scope.Contains("constant.language", StringComparison.Ordinal))
                    return (KeywordBrush, false);
            }
            return (null, false);
        }

        private static Brush Frozen(byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }

        private sealed record TokenSpan(int Start, int End, IReadOnlyList<string> Scopes);

        private sealed record LineResult(IStateStack? State, List<TokenSpan> Tokens);
    }
}
