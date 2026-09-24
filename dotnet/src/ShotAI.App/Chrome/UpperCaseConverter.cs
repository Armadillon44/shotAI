using System.Globalization;
using System.Windows.Data;

namespace ShotAI.App.Chrome;

/// <summary>
/// CSS <c>text-transform: uppercase</c> for a bound text (spec 06 7.5). Chromium upper-cases by the
/// document's language, <c>&lt;html lang="en"&gt;</c>, whatever the Windows culture, so this uses
/// the binding's culture, which is the element's <c>Language</c> (en-US unless set): a Turkish
/// Windows still gets <c>I</c> for <c>i</c>, as Electron shows.
/// </summary>
public sealed class UpperCaseConverter : IValueConverter
{
    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s ? s.ToUpper(culture) : value;

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
