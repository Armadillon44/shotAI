using System.Globalization;

namespace ShotAI.Core.SettingsUi;

/// <summary>One entry of the Auto-archive list (spec 06 2.28): the age in days, 0 for never, and its label.</summary>
public sealed record ArchiveAgeOption(int Days, string Label);

/// <summary>
/// The Auto-archive list (spec 06 2.28, 7.4): Electron's five ages, and an entry of its own for
/// a stored age that is none of them, so the list never shows a blank (EDGE-HOME-37, D-HOME-20).
/// </summary>
public static class ArchiveAgeOptions
{
    /// <summary>The five ages Electron offers, in its order (<c>Settings.tsx:882-886</c>).</summary>
    public static IReadOnlyList<ArchiveAgeOption> Standard { get; } = Array.AsReadOnly(new ArchiveAgeOption[]
    {
        new(0, "Never"),
        new(30, "After 1 month"),
        new(90, "After 3 months"),
        new(180, "After 6 months"),
        new(365, "After 1 year"),
    });

    /// <summary>
    /// The list for a stored age: <see cref="Standard"/>, with <c>After N days</c> appended when
    /// <paramref name="stored"/> is not one of its ages (a hand edit, or an older build's value).
    /// </summary>
    public static IReadOnlyList<ArchiveAgeOption> For(int stored)
    {
        foreach (var option in Standard)
        {
            if (option.Days == stored) return Standard;
        }
        return Array.AsReadOnly([.. Standard, new ArchiveAgeOption(stored, "After " + stored.ToString(CultureInfo.InvariantCulture) + " days")]);
    }
}
