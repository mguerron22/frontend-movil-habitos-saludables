using System.Globalization;

namespace HealthyMind.Mobile.Controls;

/// <summary>"#FDE4E1" → Color. El Core describe colores como texto para no depender de MAUI.</summary>
public sealed class HexColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string hex && !string.IsNullOrEmpty(hex) ? Color.FromArgb(hex) : Colors.Transparent;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}
