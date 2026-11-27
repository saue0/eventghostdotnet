using System.Globalization;
using System.Windows.Data;

namespace EventGhostDotNet;

/// <summary>
/// Assemble les enfants de l'arbre de configuration. Les modèles vivent dans <c>Core</c>, sans WPF :
/// cette conversion ne sert qu'à l'affichage et n'est jamais sérialisée.
/// </summary>
public sealed class TreeChildrenConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            ConfigurationRoot root => new CompositeCollection
            {
                new CollectionContainer { Collection = new[] { root.Startup } },
                new CollectionContainer { Collection = root.Groups }
            },
            StartupSection startup => new CompositeCollection
            {
                new CollectionContainer { Collection = startup.Configurations },
                new CollectionContainer { Collection = startup.Actions }
            },
            MacroDefinition macro => new CompositeCollection
            {
                new CollectionContainer { Collection = macro.EventItems },
                new CollectionContainer { Collection = macro.Actions }
            },
            MacroGroup group => new CompositeCollection
            {
                new CollectionContainer { Collection = group.Groups },
                new CollectionContainer { Collection = group.Macros }
            },
            _ => throw new NotSupportedException(
                $"Ce type n'a pas d'enfants d'arbre : {value?.GetType().Name ?? "null"}.")
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
