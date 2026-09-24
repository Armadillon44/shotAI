namespace ShotAI.App.Home;

/// <summary>
/// A header row of the Home list (spec 06 2.11): a date span's label or the content tier's. The
/// view writes it upper-cased with a hairline filling the rest of the line.
/// </summary>
/// <param name="Label">The label as Core gives it, before the view upper-cases it.</param>
public sealed record GroupHeaderItem(string Label);
