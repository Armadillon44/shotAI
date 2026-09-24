namespace ShotAI.App.Shell;

/// <summary>The view the main window shows (spec 06 2.1, 7.7).</summary>
public enum ShellViewKind
{
    /// <summary>The project list.</summary>
    Home,

    /// <summary>The open project's report (05).</summary>
    Project,

    /// <summary>Settings, over Home or over the open project.</summary>
    Settings,

    /// <summary>A capture session runs; the window is hidden (02, 03). Joins with the capture engine (WP-B9).</summary>
    Recording,
}
