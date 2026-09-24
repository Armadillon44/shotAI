using ShotAI.Core.Model;

namespace ShotAI.Core.Capture;

/// <summary>An element the locator examined: its UI Automation name and control type id.</summary>
public readonly record struct ElementFacts(string? Name, int ControlTypeId);

/// <summary>
/// The element-at-point rules that need no UI Automation (spec 02 2.12, 7.6): the climb that
/// chooses the element a caption may name, and the mapping of the result to the step's element.
/// A caption names an element only when its type is on the allowlist and its name is not
/// empty (INV-CAP-16); the name of an element that is not chosen is never returned.
/// </summary>
public static class ElementMapping
{
    /// <summary>
    /// The climb of 2.12.2 step 4 over the element under the point and then its control-view
    /// ancestors, nearest first: the index of the first of at most
    /// <see cref="CaptureConstants.ElementClimbDepth"/> whose name is not empty and whose type
    /// is actionable, or -1. A whitespace name is not empty (EDGE-CAP-56). The sequence is read
    /// lazily and no further than the climb goes, since each element costs a cross-process call:
    /// unlike the Rust loop, which fetches the sixth element's parent before its depth check
    /// ends the climb, the parent of the last element examined is never asked for.
    /// </summary>
    public static int Choose(IEnumerable<ElementFacts> selfThenAncestors)
    {
        ArgumentNullException.ThrowIfNull(selfThenAncestors);
        var depth = 0;
        foreach (var e in selfThenAncestors)
        {
            if (!string.IsNullOrEmpty(e.Name) && UiaControlTypes.IsActionable(e.ControlTypeId)) return depth;
            if (++depth >= CaptureConstants.ElementClimbDepth) break;
        }
        return -1;
    }

    /// <summary>
    /// The step's element for the chosen element, or the raw hit when none was chosen: the name
    /// only when chosen and not empty, and available only then; the control type's name and the
    /// bounding rectangle (all zeros when it could not be read) of the element used.
    /// </summary>
    /// <param name="chosen">Whether <paramref name="name"/> belongs to the element <see cref="Choose"/> picked.</param>
    /// <param name="name">That element's name.</param>
    /// <param name="controlTypeId">Its control type id.</param>
    /// <param name="left">Its bounding rectangle's left edge, global physical pixels.</param>
    /// <param name="top">Its top edge.</param>
    /// <param name="right">Its right edge.</param>
    /// <param name="bottom">Its bottom edge.</param>
    public static StepElement ToStep(bool chosen, string? name, int controlTypeId, double left, double top, double right, double bottom)
    {
        var shown = chosen && !string.IsNullOrEmpty(name) ? name : null;
        return new StepElement(shown is not null, shown, UiaControlTypes.Name(controlTypeId), new Rect(left, top, right - left, bottom - top));
    }

    /// <summary>What a failed or timed-out query gives the step: <see cref="StepElement.Unavailable"/>.</summary>
    public static StepElement Failed => StepElement.Unavailable;
}
