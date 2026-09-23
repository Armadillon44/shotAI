namespace ShotAI.Core.Brand;

/// <summary>
/// Role names and the CSS token names Electron's stylesheet uses for them (spec 10 2.3). Spec
/// 06 keys its WPF resources by these tokens.
/// </summary>
public static class PaletteRoles
{
    /// <summary>
    /// The 36 colour roles in the order of Electron's <c>ROLE_TO_TOKEN</c>, each with its CSS
    /// token and its <see cref="Palette"/> member.
    /// </summary>
    public static IReadOnlyList<(string Role, string Token, Func<Palette, string> Get)> All { get; } =
    [
        ("accent", "accent", p => p.Accent),
        ("accentPress", "accent-press", p => p.AccentPress),
        ("accentTint", "accent-tint", p => p.AccentTint),
        ("accentInk", "accent-ink", p => p.AccentInk),
        ("onAccent", "on-accent", p => p.OnAccent),
        ("ink", "ink", p => p.Ink),
        ("ink2", "ink-2", p => p.Ink2),
        ("ink3", "ink-3", p => p.Ink3),
        ("hair", "hair", p => p.Hair),
        ("hair2", "hair-2", p => p.Hair2),
        ("controlBd", "control-bd", p => p.ControlBd),
        ("focusRing", "focus-ring", p => p.FocusRing),
        ("accentSoft", "accent-soft", p => p.AccentSoft),
        ("surface", "surface", p => p.Surface),
        ("surface2", "surface-2", p => p.Surface2),
        ("ground", "ground", p => p.Ground),
        ("fieldBg", "field-bg", p => p.FieldBg),
        ("ok", "ok", p => p.Ok),
        ("okTint", "ok-tint", p => p.OkTint),
        ("okInk", "ok-ink", p => p.OkInk),
        ("draft", "draft", p => p.Draft),
        ("draftTint", "draft-tint", p => p.DraftTint),
        ("draftInk", "draft-ink", p => p.DraftInk),
        ("danger", "danger", p => p.Danger),
        ("dangerInk", "danger-ink", p => p.DangerInk),
        ("dangerTint", "danger-tint", p => p.DangerTint),
        ("dangerBd", "danger-bd", p => p.DangerBd),
        ("noteBg", "note-bg", p => p.NoteBg),
        ("noteBd", "note-bd", p => p.NoteBd),
        ("noteFg", "note-fg", p => p.NoteFg),
        ("cautBg", "caut-bg", p => p.CautBg),
        ("cautBd", "caut-bd", p => p.CautBd),
        ("cautFg", "caut-fg", p => p.CautFg),
        ("warnBg", "warn-bg", p => p.WarnBg),
        ("warnBd", "warn-bd", p => p.WarnBd),
        ("warnFg", "warn-fg", p => p.WarnFg),
    ];

    /// <summary>The 7 radius roles and tokens of <c>RADIUS_TO_TOKEN</c>, in its order.</summary>
    public static IReadOnlyList<(string Role, string Token)> Radii { get; } =
    [
        ("panel", "radius-panel"),
        ("card", "radius-card"),
        ("figure", "radius-figure"),
        ("control", "radius-control"),
        ("controlSm", "radius-control-sm"),
        ("micro", "radius-micro"),
        ("chip", "radius-chip"),
    ];

    /// <summary>The two type tokens (<c>TYPE_TOKENS</c>).</summary>
    public static IReadOnlyList<string> TypeTokens { get; } = ["font-stack", "label-stretch"];
}
