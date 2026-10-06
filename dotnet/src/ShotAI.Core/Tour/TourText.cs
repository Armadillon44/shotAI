using System.Globalization;

namespace ShotAI.Core.Tour;

/// <summary>
/// The tour bubble's strings other than the steps' (spec 06 2.31), as <c>Tour.tsx</c> writes
/// them, pinned by <c>TourStepsTests</c>. The step line is upper-cased by the view, as CSS's
/// <c>text-transform</c> does.
/// </summary>
public static class TourText
{
    /// <summary>The tour's accessible name (<c>aria-label</c>).</summary>
    public const string DialogName = "Getting started";

    /// <summary>Skip: finishes the tour.</summary>
    public const string Skip = "Skip";

    /// <summary>Back, from the second step on.</summary>
    public const string Back = "Back";

    /// <summary>The primary button before the last step.</summary>
    public const string Next = "Next";

    /// <summary>The primary button on the last step.</summary>
    public const string Done = "Done";

    /// <summary>The pill mock-up's label.</summary>
    public const string PillLabel = "Capturing \u00b7 3";

    /// <summary>The pill mock-up's second line.</summary>
    public const string PillHint = "Click anything \u00b7 Ctrl+Shift+S";

    /// <summary>The pill mock-up's Pause chip.</summary>
    public const string PillPause = "Pause";

    /// <summary>The pill mock-up's Stop chip.</summary>
    public const string PillStop = "Stop";

    /// <summary>The pill mock-up's discard chip.</summary>
    public const string PillDiscard = "\u2715";

    /// <summary>The bubble's step line: <c>Step 2 of 5</c> for the second of five.</summary>
    public static string StepLine(int index, int count) =>
        string.Create(CultureInfo.InvariantCulture, $"Step {index + 1} of {count}");
}
