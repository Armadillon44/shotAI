namespace ShotAI.Core.Tour;

/// <summary>
/// The tour's five steps, in order, as <c>Tour.tsx</c>'s <c>STEPS</c> has them (spec 06 2.31),
/// pinned by <c>TourStepsTests</c>. Step 5 leads with sign-in for every user, whether or not
/// the build offers it: its "if sign-in isn't offered" clause is what keeps it true (Q-SOP-10).
/// </summary>
public static class TourSteps
{
    /// <summary>The steps, in order.</summary>
    public static IReadOnlyList<TourStep> All { get; } = Array.AsReadOnly(new TourStep[]
    {
        new(
            TourAnchorId.Hero,
            "Welcome to shotAI",
            "Record a process, mark it up, and let Claude turn it into a step-by-step guide \u2014 an SOP \u2014 you can export and share. It all starts here.",
            false),
        new(
            TourAnchorId.Capture,
            "Capture your process",
            "Click \u201cCapture \u25b8\u201d to start recording. shotAI hides while you work \u2014 every click captures a screenshot and becomes a numbered step. Building from images or text instead? Use \u201cEmpty Project\u201d.",
            false),
        new(
            TourAnchorId.Mode,
            "Choose what gets captured",
            "\u201cScreen\u201d grabs a full monitor each step \u2014 the most predictable choice, and the default. Pick \u201cWindow\u201d or \u201cArea\u201d to narrow it down. \u201cAuto\u201d guesses per click and can grab extra context.",
            false),
        new(
            null,
            "Recording? Just click",
            "Once recording, a small bar stays on top. Switch to any app and click anything to capture a step \u2014 or press Ctrl+Shift+S. Pause to stop capturing, Stop to finish, the red \u2715 to discard.",
            true),
        new(
            TourAnchorId.Settings,
            "Let Claude write the guide",
            "Open \u2699 Settings \u2192 AI and choose \u201cSign in with Microsoft\u201d to use your work account. No API key needed. If sign-in isn\u2019t offered there, your organization hasn\u2019t set it up, so add your own Anthropic API key instead. Then hit \u201c\u2728 Generate SOP with Claude\u201d.",
            false),
    });
}
