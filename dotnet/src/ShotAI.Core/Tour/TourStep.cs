namespace ShotAI.Core.Tour;

/// <summary>
/// One step of the tour (spec 06 2.31): the control it spotlights, or none for a step centred
/// in a full dim; its headline and body; and whether it shows the recording pill mock-up.
/// </summary>
/// <param name="Anchor">The control the step spotlights, or null for a centred step.</param>
/// <param name="Headline">The step's heading.</param>
/// <param name="Body">The step's text.</param>
/// <param name="ShowPill">The step shows the recording pill mock-up.</param>
public sealed record TourStep(TourAnchorId? Anchor, string Headline, string Body, bool ShowPill);
