using System.Text.RegularExpressions;

namespace EventGhostDotNet.Core.Logging;

public sealed record AnsiLogRun(string Text, AnsiColor? Foreground, AnsiColor? Background);

/// <summary>
/// Convertit les séquences SGR ANSI d'un message en segments colorés.
/// L'état des couleurs est conservé d'un appel à l'autre, comme dans un terminal.
/// </summary>
public sealed class AnsiLogFormatter
{
    private static readonly Regex SgrSequence = new(@"\x1b\[([0-9;]*)m", RegexOptions.CultureInvariant);
    private static readonly AnsiColor[] Palette =
    [
        AnsiColor.FromRgb(12, 12, 12), AnsiColor.FromRgb(197, 15, 31),
        AnsiColor.FromRgb(19, 161, 14), AnsiColor.FromRgb(193, 156, 0),
        AnsiColor.FromRgb(0, 55, 218), AnsiColor.FromRgb(136, 23, 152),
        AnsiColor.FromRgb(58, 150, 221), AnsiColor.FromRgb(204, 204, 204),
        AnsiColor.FromRgb(118, 118, 118), AnsiColor.FromRgb(231, 72, 86),
        AnsiColor.FromRgb(22, 198, 12), AnsiColor.FromRgb(249, 241, 165),
        AnsiColor.FromRgb(59, 120, 255), AnsiColor.FromRgb(180, 0, 158),
        AnsiColor.FromRgb(97, 214, 214), AnsiColor.FromRgb(242, 242, 242)
    ];

    private AnsiColor? _foreground;
    private AnsiColor? _background;

    public IReadOnlyList<AnsiLogRun> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var runs = new List<AnsiLogRun>();
        var position = 0;
        foreach (Match match in SgrSequence.Matches(text))
        {
            if (match.Index > position)
                runs.Add(new AnsiLogRun(text[position..match.Index], _foreground, _background));
            ApplySgr(match.Groups[1].Value);
            position = match.Index + match.Length;
        }
        if (position < text.Length)
            runs.Add(new AnsiLogRun(text[position..], _foreground, _background));
        return runs;
    }

    private void ApplySgr(string parameters)
    {
        var parts = parameters.Split(';');
        var codes = new int[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            if (parts[index].Length == 0)
                codes[index] = 0;
            else if (!int.TryParse(parts[index], out codes[index]))
                return;
        }

        for (var index = 0; index < codes.Length; index++)
        {
            var code = codes[index];
            switch (code)
            {
                case 0:
                    _foreground = null;
                    _background = null;
                    break;
                case >= 30 and <= 37:
                    _foreground = Palette[code - 30];
                    break;
                case >= 90 and <= 97:
                    _foreground = Palette[code - 90 + 8];
                    break;
                case 39:
                    _foreground = null;
                    break;
                case >= 40 and <= 47:
                    _background = Palette[code - 40];
                    break;
                case >= 100 and <= 107:
                    _background = Palette[code - 100 + 8];
                    break;
                case 49:
                    _background = null;
                    break;
                case 38 or 48:
                    var color = ReadExtendedColor(codes, ref index);
                    if (color is not null)
                    {
                        if (code == 38)
                            _foreground = color;
                        else
                            _background = color;
                    }
                    break;
            }
        }
    }

    private static AnsiColor? ReadExtendedColor(int[] codes, ref int index)
    {
        if (index + 1 >= codes.Length)
            return null;
        var mode = codes[++index];
        if (mode == 5 && index + 1 < codes.Length)
        {
            var value = codes[++index];
            return value is >= 0 and <= 255 ? IndexedColor(value) : null;
        }
        if (mode == 2 && index + 3 < codes.Length)
        {
            var red = codes[++index];
            var green = codes[++index];
            var blue = codes[++index];
            return red is >= 0 and <= 255 && green is >= 0 and <= 255 && blue is >= 0 and <= 255
                ? AnsiColor.FromRgb((byte)red, (byte)green, (byte)blue)
                : null;
        }
        index = codes.Length;
        return null;
    }

    private static AnsiColor IndexedColor(int index)
    {
        if (index < Palette.Length)
            return Palette[index];
        if (index >= 232)
        {
            var gray = (byte)(8 + (index - 232) * 10);
            return AnsiColor.FromRgb(gray, gray, gray);
        }
        var cube = index - 16;
        static byte Component(int value) => (byte)(value == 0 ? 0 : 55 + value * 40);
        return AnsiColor.FromRgb(Component(cube / 36), Component(cube / 6 % 6), Component(cube % 6));
    }
}
