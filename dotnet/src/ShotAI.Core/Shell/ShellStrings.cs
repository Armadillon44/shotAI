using System.Globalization;

namespace ShotAI.Core.Shell;

/// <summary>
/// The shell's user-visible strings (spec 03 2.11, INV-SHELL-22), exactly as Electron shows
/// them. Each work package adds the strings of the windows it builds: the menu's and About's
/// with WP-A15, the Brand submenu's with WP-A18, the pill's with WP-B7, the overlay's with
/// WP-B8.
/// </summary>
public static class ShellStrings
{
    /// <summary>The main window's title (<c>src/main/main.ts:270</c>), and the idle pill's label.</summary>
    public const string AppName = "shotAI";

    // The menu (2.8.1): menu.ts's own labels, and Electron 42.5.0's Windows role labels for the rest.

    /// <summary>The File menu.</summary>
    public const string FileMenu = "File";

    /// <summary>File, Import Project (<c>src/main/menu.ts:212</c>).</summary>
    public const string ImportProject = "Import Project\u2026";

    /// <summary>File, Settings (<c>menu.ts:217</c>).</summary>
    public const string Settings = "Settings";

    /// <summary>File, Exit: the <c>quit</c> role's Windows label.</summary>
    public const string Exit = "Exit";

    /// <summary>The Edit menu (the <c>editMenu</c> role).</summary>
    public const string EditMenu = "Edit";

    /// <summary>Edit, Undo.</summary>
    public const string Undo = "Undo";

    /// <summary>Edit, Redo.</summary>
    public const string Redo = "Redo";

    /// <summary>Edit, Cut.</summary>
    public const string Cut = "Cut";

    /// <summary>Edit, Copy.</summary>
    public const string Copy = "Copy";

    /// <summary>Edit, Paste.</summary>
    public const string Paste = "Paste";

    /// <summary>Edit, Delete.</summary>
    public const string Delete = "Delete";

    /// <summary>Edit, Select All.</summary>
    public const string SelectAll = "Select All";

    /// <summary>The View menu (<c>menu.ts:229</c>).</summary>
    public const string ViewMenu = "View";

    /// <summary>View, Actual Size: the <c>resetZoom</c> role.</summary>
    public const string ActualSize = "Actual Size";

    /// <summary>View, Zoom In.</summary>
    public const string ZoomIn = "Zoom In";

    /// <summary>View, Zoom Out.</summary>
    public const string ZoomOut = "Zoom Out";

    /// <summary>View, Toggle Full Screen: the <c>togglefullscreen</c> role.</summary>
    public const string ToggleFullScreen = "Toggle Full Screen";

    /// <summary>View, Brand: the open project's brand pin (<c>menu.ts:242</c>).</summary>
    public const string Brand = "Brand";

    /// <summary>
    /// View, Brand's first row, which clears the pin and names the app brand the project then
    /// follows (<c>menu.ts:186</c>).
    /// </summary>
    /// <param name="brandLabel">The app brand's label: <c>shotAI</c> or <c>LFI</c>.</param>
    public static string BrandAppDefault(string brandLabel) => $"App default ({brandLabel})";

    /// <summary>The Window menu (the <c>windowMenu</c> role).</summary>
    public const string WindowMenu = "Window";

    /// <summary>Window, Minimize.</summary>
    public const string Minimize = "Minimize";

    /// <summary>Window, Close.</summary>
    public const string Close = "Close";

    /// <summary>The Help menu (the <c>help</c> role).</summary>
    public const string HelpMenu = "Help";

    /// <summary>Help, About shotAI (<c>menu.ts:254</c>).</summary>
    public const string About = "About shotAI";

    // The About dialog (2.8.4, menu.ts:136-152).

    /// <summary>The About dialog's title (<c>menu.ts:141</c>).</summary>
    public const string AboutTitle = "About shotAI";

    /// <summary>The About dialog's tagline, the first line of its detail (<c>menu.ts:144</c>).</summary>
    public const string AboutTagline = "Local-first SOP builder \u2014 capture a process and let Claude write the guide.";

    /// <summary>The About dialog's one button (<c>menu.ts:147</c>).</summary>
    public const string Ok = "OK";

    // The capture pill (2.4, src/renderer/toolbar/App.tsx) and its Discard confirmation (7.6.3).

    /// <summary>The pill's window title (<c>src/main/main.ts:325</c>, <c>toolbar.html:6</c>).</summary>
    public const string PillTitle = "shotAI \u2014 Capture";

    /// <summary>The pill's label while no session exists (<c>toolbar/App.tsx:87</c>).</summary>
    public const string PillIdleLabel = AppName;

    /// <summary>The pill's label while a session exists (<c>:88</c>).</summary>
    /// <param name="paused">Whether the session is paused.</param>
    /// <param name="count">The step count the engine reports.</param>
    /// <returns><c>Capturing \u00B7 3</c> or <c>Paused \u00B7 3</c>.</returns>
    public static string PillActiveLabel(bool paused, int count) =>
        string.Create(CultureInfo.InvariantCulture, $"{(paused ? "Paused" : "Capturing")} \u00B7 {count}");

    /// <summary>Row 2 while recording, the core interaction the hidden main window would otherwise teach (<c>:173</c>).</summary>
    public const string HintRecording = "Click anything to capture a step \u00B7 Ctrl+Shift+S";

    /// <summary>Row 2 while paused (<c>:172</c>).</summary>
    public const string HintPaused = "Paused \u2014 press Resume to keep capturing";

    /// <summary>The error row for a capture error with no message (<c>:35</c>, EDGE-SHELL-15).</summary>
    public const string ErrorFallback = "A capture failed \u2014 see the log for details.";

    /// <summary>The Pause button (<c>:102</c>).</summary>
    public const string Pause = "\u275A\u275A Pause";

    /// <summary>The Resume button (<c>:111</c>).</summary>
    public const string Resume = "\u25B6 Resume";

    /// <summary>The Stop button (<c>:120</c>).</summary>
    public const string Stop = "\u25A0 Stop";

    /// <summary>The Discard button, the pill's destructive control (<c>:130</c>).</summary>
    public const string Discard = "\u2715";

    /// <summary>The error row's glyph (<c>:151</c>).</summary>
    public const string ErrorGlyph = "\u26A0";

    /// <summary>The Pause button's tooltip (<c>:99</c>).</summary>
    public const string PauseTip = "Pause";

    /// <summary>The Resume button's tooltip (<c>:108</c>).</summary>
    public const string ResumeTip = "Resume";

    /// <summary>The Stop button's tooltip: the JSX writes <c>Stop &amp;amp; finish</c>, which JSX decodes (<c>:117</c>).</summary>
    public const string StopTip = "Stop & finish";

    /// <summary>The Discard button's tooltip and its accessible name (<c>:126-127</c>).</summary>
    public const string DiscardTip = "Discard this capture";

    /// <summary>The drag area's tooltip (<c>:82</c>).</summary>
    public const string DragTip = "Drag to move";

    /// <summary>The error row's worded dismiss, a shape that cannot be mistaken for Discard (<c>:166</c>, EDGE-SHELL-16).</summary>
    public const string Dismiss = "Dismiss";

    /// <summary>The dismiss control's tooltip (<c>:162</c>).</summary>
    public const string DismissTip = "Dismiss this error";

    /// <summary>The dismiss control's accessible name (<c>:163</c>).</summary>
    public const string DismissName = "Dismiss this capture error";

    /// <summary>The Discard confirmation when the whole project goes (<c>:67-68</c>, R5).</summary>
    public const string DiscardWholeProject = "Discard this capture? This is a new project, so the entire project will be deleted.";

    /// <summary>The Discard confirmation when only this session's steps go (<c>:69</c>).</summary>
    public const string DiscardSessionSteps = "Discard this capture? Steps recorded in this session will be deleted.";

    /// <summary>The confirmation's destructive button, worded for what it does (7.6.3, D8, Q-SHELL-7); Electron's was <c>OK</c>.</summary>
    public const string DiscardConfirm = "Discard";

    /// <summary>The confirmation's other button, the default (7.6.3).</summary>
    public const string Cancel = "Cancel";

    // The area-select overlay (2.5, src/renderer/overlay/App.tsx).

    /// <summary>The overlay's window title, its page's title (<c>overlay.html:6</c>).</summary>
    public const string OverlayTitle = "shotAI \u2014 Select area";

    /// <summary>The hint's first line, shown until the drag starts (<c>overlay/App.tsx:67</c>).</summary>
    public const string OverlayHint = "Drag to select a capture area";

    /// <summary>The hint's second line (<c>:68</c>).</summary>
    public const string OverlayHintSub = "Press Esc to cancel";

    /// <summary>
    /// The selection's size badge (<c>:83-84</c>): the width and height in physical pixels of
    /// the rectangle the selection resolves to (EDGE-SHELL-39).
    /// </summary>
    /// <param name="width">The width in physical pixels.</param>
    /// <param name="height">The height in physical pixels.</param>
    /// <returns><c>1280 \u00D7 720px</c>.</returns>
    public static string Badge(int width, int height) =>
        string.Create(CultureInfo.InvariantCulture, $"{width} \u00D7 {height}px");
}
