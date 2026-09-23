namespace ShotAI.Core.Model;

/// <summary>
/// <c>CALLOUT_GLYPH</c> (<c>src/shared/project.ts:237-242</c>): the type mark of each callout
/// kind in the app and in every export, so the kind reads even in grayscale.
/// </summary>
/// <remarks>
/// Typographic marks, never emoji presentation and never with U+FE0F, and the same code
/// points macOS uses (INV-MODEL-25). The warning mark was U+26D4 NO ENTRY until Electron
/// <c>f8a7f1a</c>, which most fonts draw as a coloured orb.
/// </remarks>
public static class CalloutGlyphs
{
    /// <summary>U+2139 INFORMATION SOURCE.</summary>
    public const string Note = "\u2139";

    /// <summary>U+26A0 WARNING SIGN.</summary>
    public const string Caution = "\u26A0";

    /// <summary>U+2501 BOX DRAWINGS HEAVY HORIZONTAL.</summary>
    public const string Warning = "\u2501";

    /// <summary>None: a section renders as a divider heading.</summary>
    public const string Section = "";

    /// <summary>
    /// The glyph of a known kind, or null for anything else. Callers narrow first
    /// (<see cref="ProjectStep.KnownCallout"/>); an unknown kind has no glyph, which is
    /// what an Office export once threw on (#90).
    /// </summary>
    public static string? For(string? kind) => kind switch
    {
        CalloutKinds.Note => Note,
        CalloutKinds.Caution => Caution,
        CalloutKinds.Warning => Warning,
        CalloutKinds.Section => Section,
        _ => null,
    };
}
