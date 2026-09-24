using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ShotAI.App.Chrome;

/// <summary>
/// A brand radius that may be a capsule (spec 06 7.5): CSS clamps <c>border-radius: 999px</c> to
/// half the height, but WPF's <c>Border</c> clamps each corner to half its own side, which draws
/// an ellipse, and <c>CornerRadius</c> refuses infinity. So a chip's corner is bound here.
/// </summary>
/// <remarks>
/// As a multi-value converter: the element's <c>ActualHeight</c>, then its radius as a
/// <c>RadiusValue.*</c> double, <see cref="double.PositiveInfinity"/> for a capsule. As a value
/// converter: the element's <c>ActualHeight</c>, always a capsule.
/// </remarks>
public sealed class CapsuleCornerConverter : IMultiValueConverter, IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        var height = values.Length > 0 && values[0] is double h ? h : 0;
        var radius = values.Length > 1 && values[1] is double r ? r : 0;
        return Corner(height, radius);
    }

    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Corner(value is double h ? h : 0, double.PositiveInfinity);

    /// <inheritdoc/>
    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    /// <summary>The corner: <paramref name="radius"/>, or half of <paramref name="height"/> for a capsule or a larger radius.</summary>
    internal static CornerRadius Corner(double height, double radius)
    {
        var half = double.IsFinite(height) && height > 0 ? height / 2 : 0;
        var corner = double.IsNaN(radius) || radius < 0 ? 0 : Math.Min(radius, half);
        return new CornerRadius(corner);
    }
}
