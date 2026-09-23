namespace ShotAI.Core.Brand;

/// <summary>
/// The 36 colour roles of one brand in one appearance, each a lowercase <c>#rrggbb</c>
/// (spec 10 2.3). The parameter order is the contract's <c>platforms.windows.colors</c>
/// order, which is the order the generator emits; <see cref="PaletteRoles.All"/> has the
/// CSS role order.
/// </summary>
public sealed record Palette(
    string Accent, string AccentPress, string AccentTint, string AccentInk, string OnAccent,
    string Ink, string Ink2, string Ink3, string Hair, string Hair2, string ControlBd,
    string Surface, string Surface2, string Ground, string FieldBg,
    string Ok, string OkTint, string OkInk, string Draft, string DraftTint, string DraftInk,
    string Danger, string DangerTint, string DangerInk,
    string NoteBg, string NoteBd, string NoteFg, string CautBg, string CautBd, string CautFg,
    string WarnBg, string WarnBd, string WarnFg,
    string FocusRing, string AccentSoft, string DangerBd);
