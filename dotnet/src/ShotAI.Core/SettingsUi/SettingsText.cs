using System.Globalization;
using ShotAI.Core.Brand;
using ShotAI.Core.Json;
using ShotAI.Core.Settings;
using ShotAI.Core.Sop;

namespace ShotAI.Core.SettingsUi;

/// <summary>
/// Settings' strings (spec 06 2.24 to 2.29, 7.4), each checked against <c>Settings.tsx</c> and
/// pinned by <c>SettingsTextTests</c> on Linux: the shell, the AI tab's switch and SOP options,
/// and the Capture, Appearance, Storage and About tabs. The sign-in and key groups' strings join
/// with WP-D7, the update check's with WP-E1, and the intro tour's with WP-B10b.
/// </summary>
/// <remarks>
/// JSX folds each line break in a text block to a space, so a hint that wraps in the source is
/// one line here. The SOP options' labels and blurbs are 07's <see cref="SopCatalog"/> and each
/// brand's label is 10's <see cref="BrandPalette"/>.
/// </remarks>
public static class SettingsText
{
    /// <summary>The bar's Back button (2.24).</summary>
    public const string Back = "\u2190 Back";

    /// <summary>The bar's heading.</summary>
    public const string Title = "Settings";

    /// <summary>The tab bar's accessible name.</summary>
    public const string TabsName = "Settings sections";

    /// <summary>The AI tab.</summary>
    public const string AiTab = "AI";

    /// <summary>The Capture tab.</summary>
    public const string CaptureTab = "Capture";

    /// <summary>The Appearance tab.</summary>
    public const string AppearanceTab = "Appearance";

    /// <summary>The Storage tab.</summary>
    public const string StorageTab = "Storage";

    /// <summary>The About tab.</summary>
    public const string AboutTab = "About";

    /// <summary>A value not read yet: the projects folder until it arrives (2.28).</summary>
    public const string NotLoaded = "\u2026";

    /// <summary>The AI tab's master switch (2.25).</summary>
    public const string AiSwitch = "AI SOP generation";

    /// <summary>The Model group's heading and its radio group's accessible name.</summary>
    public const string Model = "Model";

    /// <summary>The Tone group's heading and accessible name.</summary>
    public const string Tone = "Tone";

    /// <summary>The Effort group's heading and accessible name.</summary>
    public const string Effort = "Effort";

    /// <summary>The custom instructions group's heading and the field's accessible name.</summary>
    public const string CustomInstructions = "Custom instructions (optional)";

    /// <summary>The custom instructions field's placeholder.</summary>
    public const string CustomInstructionsPlaceholder = "e.g. Reference our ticketing system, avoid jargon, always note required permissions\u2026";

    /// <summary>The Screenshot quality group's heading and the slider's accessible name (2.26).</summary>
    public const string ScreenshotQuality = "Screenshot quality";

    /// <summary>Its hint.</summary>
    public const string ScreenshotQualityHint =
        "Downscales captured screenshots to cut file size and AI cost. Lower = smaller and cheaper but softer text; a readability floor keeps small captures legible to Claude. Applies to new captures.";

    /// <summary>The remote visibility switch.</summary>
    public const string RemoteVisible = "Show shotAI in remote sessions & screen shares";

    /// <summary>Its hint.</summary>
    public const string RemoteVisibleHint =
        "By default shotAI is hidden from all screen capture, which is what keeps it out of its own screenshots, but also makes it invisible in Teams, Splashtop, GoToAssist and similar. Turn this on to see and use the app over a remote connection. Your screenshots stay clean: shotAI still hides itself while recording, and the capture pill is excluded from each shot as it is taken.";

    /// <summary>The Theme heading and its radio group's accessible name (2.27).</summary>
    public const string Theme = "Theme";

    /// <summary>Its hint.</summary>
    public const string ThemeHint = "Choose the app\u2019s color theme. \u201cSystem\u201d follows your Windows light/dark setting.";

    /// <summary>The Brand heading and its radio group's accessible name.</summary>
    public const string Brand = "Brand";

    /// <summary>Its first hint.</summary>
    public const string BrandHint = "Which identity the app wears. Independent of light/dark \u2014 each brand has both.";

    /// <summary>Its second hint.</summary>
    public const string BrandExportsHint = "Exports follow the brand too, always in its light colors \u2014 a dark document is unreadable printed.";

    /// <summary>The Projects folder group's heading (2.28).</summary>
    public const string ProjectsFolder = "Projects folder";

    /// <summary>Its hint.</summary>
    public const string ProjectsFolderHint = "Where shotAI stores each project \u2014 screenshots, manifest, and exports.";

    /// <summary>The button that picks another folder.</summary>
    public const string ChangeFolder = "Change\u2026";

    /// <summary>The folder dialog's title.</summary>
    public const string FolderDialogTitle = "Choose shotAI projects folder";

    /// <summary>The Auto-archive group's heading.</summary>
    public const string AutoArchive = "Auto-archive old projects";

    /// <summary>Its hint.</summary>
    public const string AutoArchiveHint =
        "Projects you haven\u2019t opened or edited in this long are compressed and moved to the Archive tab automatically. They stay listed \u2014 opening one restores it. You can also archive projects manually anytime.";

    /// <summary>The archive age list's accessible name.</summary>
    public const string AutoArchiveName = "Auto-archive age";

    /// <summary>The Your name group's heading and the field's accessible name (2.29).</summary>
    public const string YourName = "Your name";

    /// <summary>Its hint.</summary>
    public const string YourNameHint = "Optionally credited on exported guides. When included, the footer reads \u201cCreated on <date> by <your name>\u201d.";

    /// <summary>The name field's placeholder.</summary>
    public const string YourNamePlaceholder = "e.g. Dana Reyes";

    /// <summary>The include-my-name switch.</summary>
    public const string IncludeName = "Include my name in reports & exports";

    /// <summary>Its hint.</summary>
    public const string IncludeNameHint = "Adds \u201cby <your name>\u201d to the \u201cCreated on \u2026\u201d line of every export. Set a name above to enable this.";

    /// <summary>The About group's heading.</summary>
    public const string About = "About";

    /// <summary>The Updates group's heading.</summary>
    public const string Updates = "Updates";

    /// <summary>The automatic update check's switch.</summary>
    public const string CheckForUpdates = "Check for updates";

    /// <summary>Its hint.</summary>
    public const string CheckForUpdatesHint =
        "Asks GitHub once a day, when shotAI starts, whether a newer version has been released, and tells you if one has. This is the only time shotAI contacts the internet on its own. It never installs anything by itself.";

    /// <summary>The theme choices in order, each with its label and the hint under the group (2.27).</summary>
    public static IReadOnlyList<(ThemePref Id, string Label, string Blurb)> Themes { get; } = Array.AsReadOnly(new[]
    {
        (ThemePref.System, "System", "Match your Windows light/dark setting."),
        (ThemePref.Light, "Light", "Always use the light theme."),
        (ThemePref.Dark, "Dark", "Always use the dark theme."),
    });

    // The blurb of each shipping brand by id (Q-HOME-11); Electron gives any other brand LFI's.
    private static readonly Dictionary<string, string> BrandBlurbs = new(StringComparer.Ordinal)
    {
        [BrandPalette.ShotAI.Id] = "shotAI\u2019s own identity \u2014 violet.",
        [BrandPalette.Lfi.Id] = "LaCrosse Footwear corporate \u2014 charcoal and rust.",
    };

    /// <summary>
    /// The master switch's hint: what turning it on needs, the sign-in below when federation is
    /// available, else the API key (2.25).
    /// </summary>
    public static string AiHint(bool federated) =>
        "Use Claude to write a step-by-step guide from your capture \u2014 this needs "
        + (federated ? "you to sign in below" : "an Anthropic API key (below)")
        + ". When off, no Claude features appear and nothing ever leaves your machine.";

    /// <summary>The line shown instead of the AI tab's groups while the switch is off.</summary>
    public static string AiOff(bool federated) =>
        "Claude SOP generation is off. Turn it on to choose a model and tone and "
        + (federated ? "sign in with your work account" : "connect your Anthropic API key")
        + ".";

    /// <summary>
    /// A text field's text as Electron's textarea gives it to script: each CR LF pair, then each
    /// lone CR, as LF (HTML's newline normalization). WPF's text box breaks a line with CR LF, so
    /// the custom instructions are counted and stored this way, as Electron counts and stores them.
    /// </summary>
    public static string TextareaValue(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    /// <summary>The custom instructions' live length, in UTF-16 code units, out of the cap: <c>12/2000</c>.</summary>
    public static string CustomInstructionsCount(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Length.ToString(CultureInfo.InvariantCulture) + "/" + SopCatalog.CustomInstructionsMax.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The quality slider's value as Electron prints it: <c>Math.round(scale * 100)</c> and a percent sign.</summary>
    public static string CaptureScaleLabel(double scale) => JsNumber.ToJsString(JsMath.Round(scale * 100)) + "%";

    /// <summary>The hint for <paramref name="theme"/> (2.27).</summary>
    public static string ThemeBlurb(ThemePref theme)
    {
        foreach (var t in Themes)
        {
            if (t.Id == theme) return t.Blurb;
        }
        throw new ArgumentOutOfRangeException(nameof(theme), theme, "Not a theme preference.");
    }

    /// <summary>
    /// The hint for <paramref name="brandId"/>: each shipping brand's own, and LFI's for any
    /// other, as Electron gives every brand but the default (EDGE-HOME-26).
    /// </summary>
    public static string BrandBlurb(string brandId)
    {
        ArgumentNullException.ThrowIfNull(brandId);
        return BrandBlurbs.TryGetValue(brandId, out var blurb) ? blurb : BrandBlurbs[BrandPalette.Lfi.Id];
    }

    /// <summary>
    /// The About line (7.12): the app's name and version, its platform and architecture, and
    /// the .NET version in place of Electron's (D-HOME-22): <c>shotAI 2.0.0 &#183; win32/x64 &#183; .NET 10.0.1</c>.
    /// </summary>
    public static string AppInfoLine(string name, string version, string platform, string arch, string dotNetVersion)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(arch);
        ArgumentNullException.ThrowIfNull(dotNetVersion);
        return name + " " + version + " \u00b7 " + platform + "/" + arch + " \u00b7 .NET " + dotNetVersion;
    }

    /// <summary>The inline error under the bar, for a failure that is not a settings write (2.24, 7.12).</summary>
    public static string Error(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return "Error: " + message;
    }
}
