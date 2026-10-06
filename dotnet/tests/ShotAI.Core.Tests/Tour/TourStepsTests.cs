using System.Text.RegularExpressions;
using ShotAI.Core.Tests.Support;
using ShotAI.Core.Tour;
using Xunit;

namespace ShotAI.Core.Tests.Tour;

/// <summary>
/// Spec 06 8.4 and 2.31: five steps, anchored to the hero, Capture, the mode row, nothing and
/// Settings, the pill on step 4 only, and every headline, body and bubble string as
/// <c>Tour.tsx</c> has it.
/// </summary>
public sealed partial class TourStepsTests
{
    [Fact]
    public void FiveStepsWithTheirAnchors()
    {
        Assert.Equal(5, TourSteps.All.Count);
        Assert.Equal([TourAnchorId.Hero, TourAnchorId.Capture, TourAnchorId.Mode, null, TourAnchorId.Settings], TourSteps.All.Select(s => s.Anchor));
        Assert.Equal([false, false, false, true, false], TourSteps.All.Select(s => s.ShowPill));
    }

    [Fact]
    public void HeadlinesAndBodiesVerbatim()
    {
        Assert.Equal(
            ["Welcome to shotAI", "Capture your process", "Choose what gets captured", "Recording? Just click", "Let Claude write the guide"],
            TourSteps.All.Select(s => s.Headline));
        Assert.Equal(
            "Record a process, mark it up, and let Claude turn it into a step-by-step guide \u2014 an SOP \u2014 you can export and share. It all starts here.",
            TourSteps.All[0].Body);
        Assert.Equal(
            "Click \u201cCapture \u25b8\u201d to start recording. shotAI hides while you work \u2014 every click captures a screenshot and becomes a numbered step. Building from images or text instead? Use \u201cEmpty Project\u201d.",
            TourSteps.All[1].Body);
        Assert.Equal(
            "\u201cScreen\u201d grabs a full monitor each step \u2014 the most predictable choice, and the default. Pick \u201cWindow\u201d or \u201cArea\u201d to narrow it down. \u201cAuto\u201d guesses per click and can grab extra context.",
            TourSteps.All[2].Body);
        Assert.Equal(
            "Once recording, a small bar stays on top. Switch to any app and click anything to capture a step \u2014 or press Ctrl+Shift+S. Pause to stop capturing, Stop to finish, the red \u2715 to discard.",
            TourSteps.All[3].Body);
        // Q-SOP-10: sign-in leads for every user, with the clause for a build that does not offer it.
        Assert.Equal(
            "Open \u2699 Settings \u2192 AI and choose \u201cSign in with Microsoft\u201d to use your work account. No API key needed. If sign-in isn\u2019t offered there, your organization hasn\u2019t set it up, so add your own Anthropic API key instead. Then hit \u201c\u2728 Generate SOP with Claude\u201d.",
            TourSteps.All[4].Body);
    }

    [Fact]
    public void StepsMatchTheElectronSource()
    {
        var source = ElectronSource.Read("src/renderer/project/Tour.tsx");
        var anchors = Anchors().Matches(source).Select(m => m.Groups["anchor"].Value).ToList();
        Assert.Equal(["'hero'", "'capture'", "'mode'", "null", "'settings'"], anchors);
        foreach (var step in TourSteps.All)
        {
            Assert.Contains($"headline: '{step.Headline}'", source, StringComparison.Ordinal);
            Assert.Contains($"body: '{step.Body}'", source, StringComparison.Ordinal);
        }
        Assert.Single(Pills().Matches(source));
    }

    [Fact]
    public void BubbleStrings()
    {
        Assert.Equal("Getting started", TourText.DialogName);
        Assert.Equal(("Skip", "Back", "Next", "Done"), (TourText.Skip, TourText.Back, TourText.Next, TourText.Done));
        Assert.Equal(("Capturing \u00b7 3", "Click anything \u00b7 Ctrl+Shift+S"), (TourText.PillLabel, TourText.PillHint));
        Assert.Equal(("Pause", "Stop", "\u2715"), (TourText.PillPause, TourText.PillStop, TourText.PillDiscard));
        Assert.Equal("Step 1 of 5", TourText.StepLine(0, 5));
        Assert.Equal("Step 5 of 5", TourText.StepLine(4, 5));
        Assert.Equal("Step 12 of 20", TourText.StepLine(11, 20));
    }

    [Fact]
    public void BubbleStringsMatchTheElectronSource()
    {
        var source = ElectronSource.Read("src/renderer/project/Tour.tsx").ReplaceLineEndings("\n");
        var text = JsxLines().Replace(source, " ");
        Assert.Contains($"aria-label=\"{TourText.DialogName}\"", source, StringComparison.Ordinal);
        Assert.Contains($"onClick={{finish}}> {TourText.Skip} </button>", text, StringComparison.Ordinal);
        Assert.Contains($"onClick={{back}}> {TourText.Back} </button>", text, StringComparison.Ordinal);
        Assert.Contains($"{{i === STEPS.length - 1 ? '{TourText.Done}' : '{TourText.Next}'}}", source, StringComparison.Ordinal);
        Assert.Contains("> Step {i + 1} of {STEPS.length} </div>", text, StringComparison.Ordinal);
        Assert.Contains($"> {TourText.PillLabel} <small>{TourText.PillHint}</small> </span>", text, StringComparison.Ordinal);
        Assert.Contains($"\"tour__pill-btn\">{TourText.PillPause}</span>", source, StringComparison.Ordinal);
        Assert.Contains($"\"tour__pill-btn tour__pill-btn--stop\">{TourText.PillStop}</span>", source, StringComparison.Ordinal);
        Assert.Contains($"\"tour__pill-btn tour__pill-btn--x\">{TourText.PillDiscard}</span>", source, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"anchor: (?<anchor>'[a-z]+'|null),")]
    private static partial Regex Anchors();

    [GeneratedRegex(@"pill: true,")]
    private static partial Regex Pills();

    // JSX text across lines: a line break and the indentation around it read as one space.
    [GeneratedRegex(@"[ \t]*\n[ \t]*")]
    private static partial Regex JsxLines();
}
