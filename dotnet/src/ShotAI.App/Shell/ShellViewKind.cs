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

    /// <summary>A capture session exists, recording or paused; the window is hidden (02, 03) unless it is shown during one (06 2.6).</summary>
    Recording,
}
