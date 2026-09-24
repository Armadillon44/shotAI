using ShotAI.App.Shell;

namespace ShotAI.App.Tests.Support;

/// <summary>
/// An <see cref="IShellNavigationState"/> the test sets field by field; <see cref="Raise"/> raises
/// <see cref="Changed"/> whether or not anything changed, as AC-SHELL-21's debug build re-raises
/// the navigation state every second.
/// </summary>
internal sealed class FakeNavigation : IShellNavigationState
{
    public bool ProjectOpen => OpenProjectPath is not null;

    public string? OpenProjectPath { get; set; }

    public string? RawProjectTheme { get; set; }

    public event EventHandler? Changed;

    /// <summary>The subscribers of <see cref="Changed"/>.</summary>
    public int Subscribers => Changed?.GetInvocationList().Length ?? 0;

    /// <summary>Sets the open project and its raw pin, then raises <see cref="Changed"/>.</summary>
    public void Set(string? openProjectPath, string? rawProjectTheme)
    {
        OpenProjectPath = openProjectPath;
        RawProjectTheme = rawProjectTheme;
        Raise();
    }

    /// <summary>Raises <see cref="Changed"/> with nothing changed.</summary>
    public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
