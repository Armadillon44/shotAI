using System.Text.RegularExpressions;
using ShotAI.Core.Brand;
using ShotAI.Core.Settings;
using ShotAI.Core.SettingsUi;
using ShotAI.Core.Sop;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.SettingsUi;

/// <summary>
/// Spec 06 8.4 (the non-auth rows of 2.24 to 2.29): each string equals the spec's text, and each
/// is found in the Electron source, JSX text that spans lines read as JSX collapses it and its
/// entities decoded. The About line names .NET (D-HOME-22) and is checked against the spec only.
/// </summary>
public sealed partial class SettingsTextTests
{
    [Fact]
    public void ShellAndTabs()
    {
        Assert.Equal("\u2190 Back", SettingsText.Back);
        Assert.Equal("Settings", SettingsText.Title);
        Assert.Equal("Settings sections", SettingsText.TabsName);
        Assert.Equal(
            ["AI", "Capture", "Appearance", "Storage", "About"],
            [SettingsText.AiTab, SettingsText.CaptureTab, SettingsText.AppearanceTab, SettingsText.StorageTab, SettingsText.AboutTab]);
        Assert.Equal("\u2026", SettingsText.NotLoaded);
        Assert.Equal("Error: Access to the path is denied.", SettingsText.Error("Access to the path is denied."));
        Assert.Equal("Error: ", SettingsText.Error(""));
        Assert.Throws<ArgumentNullException>(() => SettingsText.Error(null!));
    }

    /// <summary>2.25: the switch's hint and the off line for both federation states, byte for byte.</summary>
    [Fact]
    public void AiSwitchAndOffLine()
    {
        Assert.Equal("AI SOP generation", SettingsText.AiSwitch);
        Assert.Equal(
            "Use Claude to write a step-by-step guide from your capture \u2014 this needs you to sign in below. When off, no Claude features appear and nothing ever leaves your machine.",
            SettingsText.AiHint(federated: true));
        Assert.Equal(
            "Use Claude to write a step-by-step guide from your capture \u2014 this needs an Anthropic API key (below). When off, no Claude features appear and nothing ever leaves your machine.",
            SettingsText.AiHint(federated: false));
        Assert.Equal(
            "Claude SOP generation is off. Turn it on to choose a model and tone and sign in with your work account.",
            SettingsText.AiOff(federated: true));
        Assert.Equal(
            "Claude SOP generation is off. Turn it on to choose a model and tone and connect your Anthropic API key.",
            SettingsText.AiOff(federated: false));
    }

    /// <summary>2.25: the SOP option groups' headings and the custom instructions field.</summary>
    [Fact]
    public void SopOptions()
    {
        Assert.Equal(("Model", "Tone", "Effort"), (SettingsText.Model, SettingsText.Tone, SettingsText.Effort));
        Assert.Equal("Custom instructions (optional)", SettingsText.CustomInstructions);
        Assert.Equal("e.g. Reference our ticketing system, avoid jargon, always note required permissions\u2026", SettingsText.CustomInstructionsPlaceholder);
        Assert.Equal("0/2000", SettingsText.CustomInstructionsCount(""));
        Assert.Equal("5/2000", SettingsText.CustomInstructionsCount("Hello"));
        // UTF-16 code units, as JavaScript's length: an emoji counts two.
        Assert.Equal("2/2000", SettingsText.CustomInstructionsCount("\U0001F600"));
        Assert.Equal("2000/2000", SettingsText.CustomInstructionsCount(new string('x', SopCatalog.CustomInstructionsMax)));
        Assert.Throws<ArgumentNullException>(() => SettingsText.CustomInstructionsCount(null!));
    }

    /// <summary>2.26: the Capture tab.</summary>
    [Fact]
    public void CaptureTab()
    {
        Assert.Equal("Screenshot quality", SettingsText.ScreenshotQuality);
        Assert.Equal(
            "Downscales captured screenshots to cut file size and AI cost. Lower = smaller and cheaper but softer text; a readability floor keeps small captures legible to Claude. Applies to new captures.",
            SettingsText.ScreenshotQualityHint);
        Assert.Equal("Show shotAI in remote sessions & screen shares", SettingsText.RemoteVisible);
        Assert.Equal(
            "By default shotAI is hidden from all screen capture, which is what keeps it out of its own screenshots, but also makes it invisible in Teams, Splashtop, GoToAssist and similar. Turn this on to see and use the app over a remote connection. Your screenshots stay clean: shotAI still hides itself while recording, and the capture pill is excluded from each shot as it is taken.",
            SettingsText.RemoteVisibleHint);
    }

    /// <summary>The slider's value as <c>Math.round(scale * 100)</c> prints it; halves round up, as JavaScript's.</summary>
    [Theory]
    [InlineData(0.85, "85%")]
    [InlineData(0.5, "50%")]
    [InlineData(0.55, "55%")]
    [InlineData(1, "100%")]
    [InlineData(0.845, "85%")]
    [InlineData(0.8449, "84%")]
    [InlineData(0.8500000000000001, "85%")]
    public void CaptureScaleLabel(double scale, string label) => Assert.Equal(label, SettingsText.CaptureScaleLabel(scale));

    /// <summary>2.27: the Appearance tab, the theme blurbs in order and each brand's.</summary>
    [Fact]
    public void AppearanceTab()
    {
        Assert.Equal("Theme", SettingsText.Theme);
        Assert.Equal("Choose the app\u2019s color theme. \u201cSystem\u201d follows your Windows light/dark setting.", SettingsText.ThemeHint);
        Assert.Equal(
            [
                (ThemePref.System, "System", "Match your Windows light/dark setting."),
                (ThemePref.Light, "Light", "Always use the light theme."),
                (ThemePref.Dark, "Dark", "Always use the dark theme."),
            ],
            SettingsText.Themes);
        Assert.Equal("Always use the dark theme.", SettingsText.ThemeBlurb(ThemePref.Dark));
        Assert.Equal("Match your Windows light/dark setting.", SettingsText.ThemeBlurb(ThemePref.System));
        Assert.Throws<ArgumentOutOfRangeException>(() => SettingsText.ThemeBlurb((ThemePref)7));

        Assert.Equal("Brand", SettingsText.Brand);
        Assert.Equal("Which identity the app wears. Independent of light/dark \u2014 each brand has both.", SettingsText.BrandHint);
        Assert.Equal("Exports follow the brand too, always in its light colors \u2014 a dark document is unreadable printed.", SettingsText.BrandExportsHint);
        Assert.Equal("shotAI\u2019s own identity \u2014 violet.", SettingsText.BrandBlurb("shotAI"));
        Assert.Equal("LaCrosse Footwear corporate \u2014 charcoal and rust.", SettingsText.BrandBlurb("LFI"));
        Assert.Throws<ArgumentNullException>(() => SettingsText.BrandBlurb(null!));
    }

    /// <summary>EDGE-HOME-26: any brand but the default reads LFI's blurb, as Electron's binary choice gives it.</summary>
    [Theory]
    [InlineData("lfi")]
    [InlineData("future")]
    [InlineData("")]
    public void AnyOtherBrandReadsLfisBlurb(string brand) =>
        Assert.Equal("LaCrosse Footwear corporate \u2014 charcoal and rust.", SettingsText.BrandBlurb(brand));

    /// <summary>Every brand Settings offers has a blurb of its own (Q-HOME-11): a third brand needs one before it ships.</summary>
    [Fact]
    public void EveryShippingBrandHasItsOwnBlurb() =>
        Assert.Equal(BrandPalette.BrandIds.Count, BrandPalette.BrandIds.Select(SettingsText.BrandBlurb).Distinct().Count());

    /// <summary>2.28: the Storage tab.</summary>
    [Fact]
    public void StorageTab()
    {
        Assert.Equal("Projects folder", SettingsText.ProjectsFolder);
        Assert.Equal("Where shotAI stores each project \u2014 screenshots, manifest, and exports.", SettingsText.ProjectsFolderHint);
        Assert.Equal("Change\u2026", SettingsText.ChangeFolder);
        Assert.Equal("Choose shotAI projects folder", SettingsText.FolderDialogTitle);
        Assert.Equal("Auto-archive old projects", SettingsText.AutoArchive);
        Assert.Equal(
            "Projects you haven\u2019t opened or edited in this long are compressed and moved to the Archive tab automatically. They stay listed \u2014 opening one restores it. You can also archive projects manually anytime.",
            SettingsText.AutoArchiveHint);
        Assert.Equal("Auto-archive age", SettingsText.AutoArchiveName);
    }

    /// <summary>2.29: the About tab's name, include, About and Updates groups.</summary>
    [Fact]
    public void AboutTab()
    {
        Assert.Equal("Your name", SettingsText.YourName);
        Assert.Equal("Optionally credited on exported guides. When included, the footer reads \u201cCreated on <date> by <your name>\u201d.", SettingsText.YourNameHint);
        Assert.Equal("e.g. Dana Reyes", SettingsText.YourNamePlaceholder);
        Assert.Equal("Include my name in reports & exports", SettingsText.IncludeName);
        Assert.Equal("Adds \u201cby <your name>\u201d to the \u201cCreated on \u2026\u201d line of every export. Set a name above to enable this.", SettingsText.IncludeNameHint);
        Assert.Equal("About", SettingsText.About);
        Assert.Equal("Updates", SettingsText.Updates);
        Assert.Equal("Check for updates", SettingsText.CheckForUpdates);
        Assert.Equal(
            "Asks GitHub once a day, when shotAI starts, whether a newer version has been released, and tells you if one has. This is the only time shotAI contacts the internet on its own. It never installs anything by itself.",
            SettingsText.CheckForUpdatesHint);
    }

    /// <summary>7.12: the About line names .NET where Electron named itself (D-HOME-22).</summary>
    [Fact]
    public void AppInfoLine()
    {
        Assert.Equal("shotAI 2.0.0 \u00b7 win32/x64 \u00b7 .NET 10.0.1", SettingsText.AppInfoLine("shotAI", "2.0.0", "win32", "x64", "10.0.1"));
        Assert.Equal("shotAI 2.1.0-beta.2 \u00b7 win32/arm64 \u00b7 .NET 10.0.12", SettingsText.AppInfoLine("shotAI", "2.1.0-beta.2", "win32", "arm64", "10.0.12"));
        Assert.Throws<ArgumentNullException>(() => SettingsText.AppInfoLine(null!, "v", "p", "a", "d"));
        Assert.Throws<ArgumentNullException>(() => SettingsText.AppInfoLine("n", null!, "p", "a", "d"));
        Assert.Throws<ArgumentNullException>(() => SettingsText.AppInfoLine("n", "v", null!, "a", "d"));
        Assert.Throws<ArgumentNullException>(() => SettingsText.AppInfoLine("n", "v", "p", null!, "d"));
        Assert.Throws<ArgumentNullException>(() => SettingsText.AppInfoLine("n", "v", "p", "a", null!));
    }

    /// <summary>Every string above as <c>Settings.tsx</c> and <c>ipc.ts</c> write it.</summary>
    [Fact]
    public void LiteralsMatchTheElectronSource()
    {
        var source = ElectronSource.Read("src/renderer/project/Settings.tsx").ReplaceLineEndings("\n");
        var text = JsxLines().Replace(source, " ");
        Assert.Contains($"> {SettingsText.Back} </button>", text, StringComparison.Ordinal);
        Assert.Contains($"<h2 className=\"settings__title\">{SettingsText.Title}</h2>", source, StringComparison.Ordinal);
        Assert.Contains($"aria-label=\"{SettingsText.TabsName}\"", source, StringComparison.Ordinal);
        foreach (var (id, label) in new[] { ("ai", SettingsText.AiTab), ("capture", SettingsText.CaptureTab), ("appearance", SettingsText.AppearanceTab), ("storage", SettingsText.StorageTab), ("about", SettingsText.AboutTab) })
            Assert.Contains($"{{ id: '{id}', label: '{label}' }}", source, StringComparison.Ordinal);
        Assert.Contains("<p className=\"project__error\">Error: {error}</p>", source, StringComparison.Ordinal);
        Assert.Contains($"{{projectsDir || '{SettingsText.NotLoaded}'}}", source, StringComparison.Ordinal);

        Assert.Contains($"<strong>{SettingsText.AiSwitch}</strong>", source, StringComparison.Ordinal);
        Assert.Contains(
            "> Use Claude to write a step-by-step guide from your capture \u2014 this needs {fed ? 'you to sign in below' : 'an Anthropic API key (below)'}. When off, no Claude features appear and nothing ever leaves your machine. </span>",
            text, StringComparison.Ordinal);
        Assert.Contains(
            "> Claude SOP generation is off. Turn it on to choose a model and tone and {fed ? 'sign in with your work account' : 'connect your Anthropic API key'}. </p>",
            text, StringComparison.Ordinal);
        foreach (var heading in new[] { SettingsText.Model, SettingsText.Tone, SettingsText.Effort, SettingsText.CustomInstructions })
            Assert.Contains($"<h3 className=\"settings__h\">{heading}</h3>", source, StringComparison.Ordinal);
        foreach (var group in new[] { SettingsText.Model, SettingsText.Tone, SettingsText.Effort, SettingsText.Theme, SettingsText.Brand })
            Assert.Contains($"role=\"radiogroup\" aria-label=\"{group}\"", source, StringComparison.Ordinal);
        Assert.Contains($"placeholder=\"{SettingsText.CustomInstructionsPlaceholder}\"", source, StringComparison.Ordinal);
        Assert.Contains("{sop.customInstructions.length}/{SOP_CUSTOM_INSTRUCTIONS_MAX}", source, StringComparison.Ordinal);

        Assert.Contains($"<h3 className=\"settings__h\">{SettingsText.ScreenshotQuality}</h3>", source, StringComparison.Ordinal);
        Assert.Contains($"aria-label=\"{SettingsText.ScreenshotQuality}\"", source, StringComparison.Ordinal);
        Assert.Contains($"> {SettingsText.ScreenshotQualityHint} </p>", text, StringComparison.Ordinal);
        Assert.Contains("{Math.round(captureScale * 100)}%", source, StringComparison.Ordinal);
        Assert.Contains($"<strong>{Html(SettingsText.RemoteVisible)}</strong>", source, StringComparison.Ordinal);
        Assert.Contains($"> {SettingsText.RemoteVisibleHint} </span>", text, StringComparison.Ordinal);

        Assert.Contains($"<h3 className=\"settings__h\">{SettingsText.Theme}</h3>", source, StringComparison.Ordinal);
        Assert.Contains($"> {SettingsText.ThemeHint} </p>", text, StringComparison.Ordinal);
        foreach (var (id, label, blurb) in SettingsText.Themes)
            Assert.Contains($"{{ id: '{id.ToString().ToLowerInvariant()}', label: '{label}', blurb: '{blurb}' }}", source, StringComparison.Ordinal);
        Assert.Contains($"<h3 className=\"settings__h\">{SettingsText.Brand}</h3>", source, StringComparison.Ordinal);
        Assert.Contains($"> {SettingsText.BrandHint} </p>", text, StringComparison.Ordinal);
        Assert.Contains($"> {SettingsText.BrandExportsHint} </p>", text, StringComparison.Ordinal);
        Assert.Contains($"? '{SettingsText.BrandBlurb("shotAI")}'", source, StringComparison.Ordinal);
        Assert.Contains($": '{SettingsText.BrandBlurb("LFI")}',", source, StringComparison.Ordinal);

        Assert.Contains($"<h3 className=\"settings__h\">{SettingsText.ProjectsFolder}</h3>", source, StringComparison.Ordinal);
        Assert.Contains($"> {SettingsText.ProjectsFolderHint} </p>", text, StringComparison.Ordinal);
        Assert.Contains($"> {SettingsText.ChangeFolder} </button>", text, StringComparison.Ordinal);
        Assert.Contains($"<h3 className=\"settings__h\">{SettingsText.AutoArchive}</h3>", source, StringComparison.Ordinal);
        Assert.Contains($"> {SettingsText.AutoArchiveHint} </p>", text, StringComparison.Ordinal);
        Assert.Contains($"aria-label=\"{SettingsText.AutoArchiveName}\"", source, StringComparison.Ordinal);

        Assert.Contains($"<h3 className=\"settings__h\">{SettingsText.YourName}</h3>", source, StringComparison.Ordinal);
        Assert.Contains($"> {Html(SettingsText.YourNameHint)} </p>", text, StringComparison.Ordinal);
        Assert.Contains($"placeholder=\"{SettingsText.YourNamePlaceholder}\"", source, StringComparison.Ordinal);
        Assert.Contains($"maxLength={{{SettingsDefaults.UserNameMax}}}", source, StringComparison.Ordinal);
        Assert.Contains($"<strong>{Html(SettingsText.IncludeName)}</strong>", source, StringComparison.Ordinal);
        Assert.Contains($"> {Html(SettingsText.IncludeNameHint)} </span>", text, StringComparison.Ordinal);
        Assert.Contains($"<h3 className=\"settings__h\">{SettingsText.About}</h3>", source, StringComparison.Ordinal);
        Assert.Contains($"<h3 className=\"settings__h\">{SettingsText.Updates}</h3>", source, StringComparison.Ordinal);
        Assert.Contains($"<strong>{SettingsText.CheckForUpdates}</strong>", source, StringComparison.Ordinal);
        Assert.Contains($"> {SettingsText.CheckForUpdatesHint} </span>", text, StringComparison.Ordinal);

        var ipc = ElectronSource.Read("src/main/ipc.ts");
        Assert.Contains($"title: '{SettingsText.FolderDialogTitle}',", ipc, StringComparison.Ordinal);
    }

    // JSX's entities as the source writes them.
    private static string Html(string text) => text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);

    // JSX text across lines: a line break and the indentation around it read as one space.
    [GeneratedRegex(@"[ \t]*\n[ \t]*")]
    private static partial Regex JsxLines();
}
