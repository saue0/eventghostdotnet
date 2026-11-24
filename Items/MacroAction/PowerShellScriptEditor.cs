using System.Globalization;
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
        _editor.TextArea.TextView.ElementGenerators.Add(
            new EmojiElementGenerator(_editor.Document, _editor.FontSize));

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
    /// Remplace chaque séquence emoji par une image vectorielle : le moteur texte de WPF
    /// ne sait pas rendre les polices couleur, les emoji y seraient dessinés en noir et blanc.
    /// Le texte du document n'est pas modifié, seuls les glyphes affichés changent.
    /// </summary>
    private sealed class EmojiElementGenerator : VisualLineElementGenerator
    {
        private const double EmojiScale = 1.2;

        private readonly TextDocument _document;
        private readonly double _fontSize;
        private readonly Dictionary<string, ImageSource> _images = new(StringComparer.Ordinal);

        public EmojiElementGenerator(TextDocument document, double fontSize)
        {
            _document = document;
            _fontSize = fontSize;
        }

        public override int GetFirstInterestedOffset(int startOffset)
        {
            var line = CurrentContext.VisualLine.LastDocumentLine;
            if (startOffset >= line.EndOffset)
                return -1;

            var text = _document.GetText(line.Offset, line.Length);
            var index = FindEmoji(text, startOffset - line.Offset);
            return index < 0 ? -1 : line.Offset + index;
        }

        public override VisualLineElement? ConstructElement(int offset)
        {
            var line = _document.GetLineByOffset(offset);
            var index = offset - line.Offset;
            var text = _document.GetText(line.Offset, line.Length);
            if (index < 0 || index >= text.Length)
                return null;

            var cluster = StringInfo.GetNextTextElement(text, index);
            if (!IsEmoji(cluster))
                return null;

            var size = Math.Round(_fontSize * EmojiScale);
            var image = new System.Windows.Controls.Image
            {
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                Source = GetImage(cluster)
            };
            return new InlineObjectElement(cluster.Length, image);
        }

        /// <summary>Image colorée d'une séquence emoji, mise en cache par séquence.</summary>
        private ImageSource GetImage(string emoji)
        {
            if (_images.TryGetValue(emoji, out var cached))
                return cached;

            var image = new DrawingImage();
            Emoji.Wpf.Image.SetSource(image, emoji);
            _images[emoji] = image;
            return image;
        }

        private static int FindEmoji(string text, int start)
        {
            var index = start;
            while (index < text.Length)
            {
                var cluster = StringInfo.GetNextTextElement(text, index);
                if (IsEmoji(cluster))
                    return index;
                index += Math.Max(cluster.Length, 1);
            }
            return -1;
        }

        private static bool IsEmoji(string cluster)
        {
            foreach (var rune in cluster.EnumerateRunes())
                if (IsEmojiCodePoint(rune.Value))
                    return true;
            return false;
        }

        private static bool IsEmojiCodePoint(int value) => value switch
        {
            >= 0x1F000 and <= 0x1FAFF => true, // pictogrammes, drapeaux, symboles complémentaires
            >= 0x2600 and <= 0x27BF => true,   // symboles divers et dingbats
            >= 0x2B00 and <= 0x2BFF => true,
            >= 0x231A and <= 0x23FF => true,
            0x25AA or 0x25AB or 0x25B6 or 0x25C0 => true,
            >= 0x25FB and <= 0x25FE => true,
            0x203C or 0x2049 or 0x2122 or 0x2139 or 0x3030 or 0x303D or 0x3297 or 0x3299 => true,
            _ => false
        };
    }

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
