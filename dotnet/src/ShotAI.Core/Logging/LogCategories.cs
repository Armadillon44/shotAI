namespace ShotAI.Core.Logging;

/// <summary>
/// The labels of <c>shotai.log</c> and how a logger's category maps to one (spec 10 7.5.3). The
/// labels are Electron's scopes, with <c>svc</c> in place of <c>ipc</c> (spec 11 7.11 L1).
/// </summary>
/// <remarks>
/// A category is a type's full name (<c>ILogger&lt;T&gt;</c>) and maps by the longest prefix
/// that is a whole namespace of it, so <c>ShotAI.Core.SettingsUi</c> is not under
/// <c>ShotAI.Core.Settings</c>. A category equal to a label maps to itself
/// (<c>CreateLogger("claude")</c>), <see cref="Banner"/> maps to the empty label, and everything
/// else, the empty category included, is <see cref="Main"/>.
/// </remarks>
public static class LogCategories
{
    /// <summary>
    /// The reserved category of the startup banner lines, the only one with the empty label, so
    /// a filter rule can name it. It must pass the <c>Information</c> minimum (spec 10 7.5.4).
    /// </summary>
    public const string Banner = "ShotAI.Banner";

    /// <summary>The App, updates, exports and every namespace without a row.</summary>
    public const string Main = "main";

    /// <summary>The store and settings.</summary>
    public const string Projects = "projects";

    /// <summary>Capture, Core and Platform.</summary>
    public const string Capture = "capture";

    /// <summary>SOP generation and authentication.</summary>
    public const string Claude = "claude";

    /// <summary>OCR and redaction.</summary>
    public const string Ocr = "ocr";

    /// <summary>Threading, links and the boundary <c>call:</c> lines of <see cref="ServiceLog"/>.</summary>
    public const string Svc = "svc";

    /// <summary>
    /// The longest a label may be: <c>projects</c>. <see cref="FileLogLineFormatter.ScopeWidth"/>
    /// is this plus 3, so a longer label would need every line widened.
    /// </summary>
    public const int MaxLabelLength = 8;

    /// <summary>Every label but the banner's empty one.</summary>
    public static IReadOnlyList<string> Labels { get; } = Array.AsReadOnly([Main, Projects, Capture, Claude, Ocr, Svc]);

    /// <summary>The namespace table of spec 10 7.5.3, each a namespace of ARCHITECTURE 2.4.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Prefixes { get; } = Array.AsReadOnly<KeyValuePair<string, string>>(
    [
        new("ShotAI.Core.Store", Projects),
        new("ShotAI.Core.Settings", Projects),
        new("ShotAI.Core.Capture", Capture),
        new("ShotAI.Platform.Capture", Capture),
        new("ShotAI.Core.Sop", Claude),
        new("ShotAI.Core.Auth", Claude),
        new("ShotAI.Platform.Auth", Claude),
        new("ShotAI.Platform.Ocr", Ocr),
        new("ShotAI.Core.Redaction", Ocr),
        new("ShotAI.Core.Threading", Svc),
        new("ShotAI.Core.Links", Svc),
    ]);

    /// <summary>The label a logger of <paramref name="category"/> writes.</summary>
    public static string LabelFor(string category)
    {
        ArgumentNullException.ThrowIfNull(category);
        if (string.Equals(category, Banner, StringComparison.Ordinal)) return "";
        foreach (var label in Labels)
        {
            if (string.Equals(category, label, StringComparison.Ordinal)) return label;
        }
        var best = Main;
        var bestLength = -1;
        foreach (var (prefix, label) in Prefixes)
        {
            if (prefix.Length > bestLength && IsInNamespace(category, prefix))
            {
                best = label;
                bestLength = prefix.Length;
            }
        }
        return best;
    }

    // The category is the namespace itself or a name inside it: a prefix only at a dot.
    private static bool IsInNamespace(string category, string ns) =>
        category.StartsWith(ns, StringComparison.Ordinal)
        && (category.Length == ns.Length || category[ns.Length] == '.');
}
