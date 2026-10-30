using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace JsonForge.Converters;

/// <summary>
/// Converts <see cref="bool"/> to <see cref="Visibility"/>.
/// <see cref="Invert"/> flips the result (true becomes Collapsed).
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isTrue = value is bool b && b;
        if (Invert)
        {
            isTrue = !isTrue;
        }

        return isTrue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        var isVisible = value is Visibility v && v == Visibility.Visible;
        if (Invert)
        {
            isVisible = !isVisible;
        }

        return isVisible;
    }
}