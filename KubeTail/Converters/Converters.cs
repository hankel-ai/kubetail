using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace KubeTail.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value switch
        {
            bool b => b ? Visibility.Visible : Visibility.Collapsed,
            int i => i > 0 ? Visibility.Visible : Visibility.Collapsed,
            _ => Visibility.Collapsed
        };
    public object ConvertBack(object value, Type t, object p, CultureInfo c) =>
        value is Visibility.Visible;
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is not true;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => value is not true;
}

public class SourceColorConverter : IValueConverter
{
    private static readonly Color[] Palette = new[]
    {
        Color.FromRgb(86, 156, 214),   // blue
        Color.FromRgb(78, 201, 176),   // teal
        Color.FromRgb(220, 220, 170),  // yellow
        Color.FromRgb(206, 145, 120),  // orange
        Color.FromRgb(181, 137, 214),  // purple
        Color.FromRgb(100, 200, 100),  // green
        Color.FromRgb(214, 157, 133),  // salmon
        Color.FromRgb(128, 188, 220),  // light blue
        Color.FromRgb(220, 160, 220),  // pink
        Color.FromRgb(180, 210, 130),  // lime
    };

    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        if (value is not string tag || string.IsNullOrEmpty(tag))
            return new SolidColorBrush(Colors.Gray);
        var hash = Math.Abs(tag.GetHashCode());
        return new SolidColorBrush(Palette[hash % Palette.Length]);
    }

    public object ConvertBack(object value, Type t, object p, CultureInfo c) =>
        throw new NotImplementedException();
}

public class HighlightBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is true ? new SolidColorBrush(Color.FromArgb(60, 255, 255, 0)) : Brushes.Transparent;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) =>
        throw new NotImplementedException();
}

public class NewLineBackgroundConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type t, object p, CultureInfo c)
    {
        var isNew = values.Length > 0 && values[0] is true;
        var isHighlighted = values.Length > 1 && values[1] is true;
        if (isHighlighted) return new SolidColorBrush(Color.FromArgb(60, 255, 255, 0));
        if (isNew) return new SolidColorBrush(Color.FromArgb(30, 80, 210, 130));
        return Brushes.Transparent;
    }
    public object[] ConvertBack(object value, Type[] t, object p, CultureInfo c) =>
        throw new NotImplementedException();
}

public class BoolToWrapConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is true ? TextWrapping.Wrap : TextWrapping.NoWrap;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) =>
        throw new NotImplementedException();
}

public class NullableBoolConverter : IValueConverter
{
    public object? Convert(object value, Type t, object p, CultureInfo c) => value as bool?;
    public object? ConvertBack(object value, Type t, object p, CultureInfo c) => value as bool?;
}
