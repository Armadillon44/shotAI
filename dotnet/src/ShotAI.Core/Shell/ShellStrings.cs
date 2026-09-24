namespace ShotAI.Core.Shell;

/// <summary>
/// The shell's user-visible strings (spec 03 2.11, INV-SHELL-22), exactly as Electron shows
/// them. Each work package adds the strings of the windows it builds: the menu's and About's
/// with WP-A15, the Brand submenu's with WP-A18, the pill's with WP-B6, the overlay's with the
/// area selection.
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
}
