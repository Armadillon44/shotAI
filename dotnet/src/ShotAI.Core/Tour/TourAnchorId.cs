namespace ShotAI.Core.Tour;

/// <summary>
/// The Home controls a tour step spotlights (spec 06 2.31): Electron's <c>data-tour</c> values,
/// <c>hero</c>, <c>capture</c>, <c>mode</c> and <c>settings</c>.
/// </summary>
public enum TourAnchorId
{
    /// <summary>The create hero: the heading, the mission, the name box with its buttons and the mode row.</summary>
    Hero,

    /// <summary>The hero's Capture button.</summary>
    Capture,

    /// <summary>The capture-mode chip row.</summary>
    Mode,

    /// <summary>The header's Settings button.</summary>
    Settings,
}
