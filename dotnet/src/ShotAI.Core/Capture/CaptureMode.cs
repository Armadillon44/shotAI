namespace ShotAI.Core.Capture;

/// <summary>
/// What each capture of a recording grabs (spec 06 2.4, 02 2.1): the Home picker's four modes, in
/// its chip order (<c>MODE_OPTIONS</c>, <c>App.tsx:30-35</c>). A <see cref="Model.CaptureTarget"/>
/// carries the mode by its manifest name, which <c>CaptureReadiness.BuildTarget</c> writes.
/// </summary>
public enum CaptureMode
{
    /// <summary>One full monitor each step (<c>screen</c>).</summary>
    Screen,

    /// <summary>Best-effort, classified per click (<c>auto</c>, 02 2.8.1).</summary>
    Auto,

    /// <summary>One chosen window each step (<c>window</c>).</summary>
    Window,

    /// <summary>A fixed region dragged out once (<c>area</c>).</summary>
    Area,
}
