using System.ComponentModel;
using System.Security;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using SystemColors = System.Windows.SystemColors;

namespace EventGhostDotNet.Theme;

/// <summary>
/// Applique la palette claire, sombre ou à contraste élevé aux ressources de l'application.
/// Le thème suit les réglages Windows (mode sombre des applications et contraste élevé)
/// et se réapplique lorsque l'utilisateur les modifie.
/// </summary>
internal static class AppTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static bool _hooked;

    public static void Apply()
    {
        var application = Application.Current;
        if (application is null)
            return;

        Hook();
        if (application.Dispatcher.CheckAccess())
            ApplyPalette(application);
        else
            application.Dispatcher.BeginInvoke(new Action(() => ApplyPalette(application)));
    }

    private static void Hook()
    {
        if (_hooked)
            return;
        _hooked = true;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private static void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
            Apply();
    }

    private static void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or
            UserPreferenceCategory.VisualStyle or UserPreferenceCategory.Accessibility)
            Apply();
    }

    private static void ApplyPalette(Application application)
    {
        var palette = SystemParameters.HighContrast ? HighContrastPalette()
            : IsDarkAppTheme() ? DarkPalette()
            : LightPalette();

        foreach (var (key, brush) in palette)
            application.Resources[key] = brush;
    }

    /// <summary>Pinceau du thème courant, ou <paramref name="fallback"/> hors application WPF.</summary>
    public static Brush GetBrush(string key, Brush fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? fallback;

    private static bool IsDarkAppTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException)
        {
            // Valeur absente ou clé inaccessible : le thème clair reste le choix par défaut.
            return false;
        }
    }

    private static Dictionary<string, Brush> LightPalette() => new(StringComparer.Ordinal)
    {
        ["AccentBrush"] = Rgb(0x25, 0x63, 0xEB),
        ["AccentHoverBrush"] = Rgb(0x1D, 0x4E, 0xD8),
        ["AccentPressedBrush"] = Rgb(0x1E, 0x40, 0xAF),
        ["AccentTextBrush"] = Rgb(0xFF, 0xFF, 0xFF),
        ["AccentSoftBrush"] = Rgb(0xEF, 0xF6, 0xFF),
        ["AccentSoftBorderBrush"] = Rgb(0x93, 0xC5, 0xFD),
        ["SelectionBackgroundBrush"] = Rgb(0xDB, 0xEA, 0xFE),
        ["SelectionTextBrush"] = Rgb(0x1D, 0x4E, 0xD8),
        ["WindowBackgroundBrush"] = Rgb(0xF3, 0xF5, 0xF8),
        ["ControlBackgroundBrush"] = Rgb(0xFF, 0xFF, 0xFF),
        ["ControlBorderBrush"] = Rgb(0xD7, 0xDD, 0xE6),
        ["PrimaryTextBrush"] = Rgb(0x20, 0x29, 0x38),
        ["SecondaryTextBrush"] = Rgb(0x47, 0x54, 0x67),
        ["MutedTextBrush"] = Rgb(0x98, 0xA2, 0xB3),
        ["HoverBackgroundBrush"] = Rgb(0xF2, 0xF6, 0xFC),
        ["DisabledBackgroundBrush"] = Rgb(0xF2, 0xF4, 0xF7),
        ["DisabledBorderBrush"] = Rgb(0xEA, 0xEC, 0xF0),
        ["DisabledTextBrush"] = Rgb(0x98, 0xA2, 0xB3),
        ["ErrorTextBrush"] = Rgb(0xD9, 0x2D, 0x20),
        ["SectionTextBrush"] = Rgb(0x46, 0x65, 0x86),
        ["MacroTextBrush"] = Rgb(0x2F, 0x6F, 0x9F),
        ["WarningTextBrush"] = Rgb(0xB7, 0x79, 0x1F),
        ["CodeBackgroundBrush"] = Rgb(0xF0, 0xF0, 0xF0),
        ["RuleBrush"] = Rgb(0xBB, 0xBB, 0xBB)
    };

    private static Dictionary<string, Brush> DarkPalette() => new(StringComparer.Ordinal)
    {
        ["AccentBrush"] = Rgb(0x3B, 0x82, 0xF6),
        ["AccentHoverBrush"] = Rgb(0x60, 0xA5, 0xFA),
        ["AccentPressedBrush"] = Rgb(0x1D, 0x4E, 0xD8),
        ["AccentTextBrush"] = Rgb(0x0B, 0x12, 0x20),
        ["AccentSoftBrush"] = Rgb(0x1E, 0x29, 0x3B),
        ["AccentSoftBorderBrush"] = Rgb(0x3B, 0x82, 0xF6),
        ["SelectionBackgroundBrush"] = Rgb(0x1E, 0x3A, 0x5F),
        ["SelectionTextBrush"] = Rgb(0xDB, 0xEA, 0xFE),
        ["WindowBackgroundBrush"] = Rgb(0x17, 0x1B, 0x22),
        ["ControlBackgroundBrush"] = Rgb(0x1F, 0x25, 0x2E),
        ["ControlBorderBrush"] = Rgb(0x3A, 0x44, 0x52),
        ["PrimaryTextBrush"] = Rgb(0xE6, 0xEA, 0xF0),
        ["SecondaryTextBrush"] = Rgb(0xB4, 0xBE, 0xCC),
        ["MutedTextBrush"] = Rgb(0x8A, 0x96, 0xA6),
        ["HoverBackgroundBrush"] = Rgb(0x28, 0x31, 0x3D),
        ["DisabledBackgroundBrush"] = Rgb(0x23, 0x29, 0x32),
        ["DisabledBorderBrush"] = Rgb(0x30, 0x38, 0x44),
        ["DisabledTextBrush"] = Rgb(0x8A, 0x96, 0xA6),
        ["ErrorTextBrush"] = Rgb(0xF9, 0x70, 0x66),
        ["SectionTextBrush"] = Rgb(0x9F, 0xB3, 0xCC),
        ["MacroTextBrush"] = Rgb(0x7F, 0xB3, 0xE8),
        ["WarningTextBrush"] = Rgb(0xE8, 0xB4, 0x5A),
        ["CodeBackgroundBrush"] = Rgb(0x26, 0x2D, 0x38),
        ["RuleBrush"] = Rgb(0x4A, 0x55, 0x63)
    };

    /// <summary>
    /// En contraste élevé, seules les couleurs système sont utilisées : le survol ne change
    /// que la bordure pour ne jamais poser de texte sur un fond non prévu par le thème.
    /// </summary>
    private static Dictionary<string, Brush> HighContrastPalette()
    {
        var window = SystemColors.WindowBrush;
        return new Dictionary<string, Brush>(StringComparer.Ordinal)
        {
            ["AccentBrush"] = SystemColors.HighlightBrush,
            ["AccentHoverBrush"] = SystemColors.HighlightBrush,
            ["AccentPressedBrush"] = SystemColors.HighlightBrush,
            ["AccentTextBrush"] = SystemColors.HighlightTextBrush,
            ["AccentSoftBrush"] = window,
            ["AccentSoftBorderBrush"] = SystemColors.HighlightBrush,
            ["SelectionBackgroundBrush"] = SystemColors.HighlightBrush,
            ["SelectionTextBrush"] = SystemColors.HighlightTextBrush,
            ["WindowBackgroundBrush"] = window,
            ["ControlBackgroundBrush"] = window,
            ["ControlBorderBrush"] = SystemColors.WindowTextBrush,
            ["PrimaryTextBrush"] = SystemColors.WindowTextBrush,
            ["SecondaryTextBrush"] = SystemColors.WindowTextBrush,
            ["MutedTextBrush"] = SystemColors.GrayTextBrush,
            ["HoverBackgroundBrush"] = window,
            ["DisabledBackgroundBrush"] = window,
            ["DisabledBorderBrush"] = SystemColors.GrayTextBrush,
            ["DisabledTextBrush"] = SystemColors.GrayTextBrush,
            ["ErrorTextBrush"] = SystemColors.WindowTextBrush,
            ["SectionTextBrush"] = SystemColors.WindowTextBrush,
            ["MacroTextBrush"] = SystemColors.WindowTextBrush,
            ["WarningTextBrush"] = SystemColors.WindowTextBrush,
            ["CodeBackgroundBrush"] = window,
            ["RuleBrush"] = SystemColors.WindowTextBrush
        };
    }

    private static SolidColorBrush Rgb(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
