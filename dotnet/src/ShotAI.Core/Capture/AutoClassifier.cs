using ShotAI.Core.Json;

namespace ShotAI.Core.Capture;

/// <summary>
/// <c>captureModeFor</c> (spec 02 2.8.1, <c>src/main/capture-geometry.ts:39-47</c>): how an
/// auto-mode capture frames a click, from the foreground window's app name and title.
/// </summary>
public static class AutoClassifier
{
    // SHELL_HOST_RE's alternatives, lower case. The regex is tested against the owner name,
    // which is sometimes the friendly name and sometimes the executable.
    private static readonly string[] ShellHosts =
    [
        "experience host",
        "searchhost",
        "shellexperiencehost",
        "startmenuexperiencehost",
        "searchapp",
        "textinputhost",
        "cortana",
    ];

    /// <summary>The frame for <paramref name="active"/>; unknown focus captures the whole monitor, never a guessed crop.</summary>
    public static AutoMode Classify(ForegroundInfo? active) =>
        active is null ? AutoMode.Fullscreen : Classify(active.App, active.Title);

    /// <summary>
    /// The rules in order: the desktop (<c>Windows Explorer</c>, <c>Program Manager</c>) is
    /// <see cref="AutoMode.Fullscreen"/>; Explorer with a blank title (the taskbar and tray) and
    /// the shell hosts (Start, Search and their kin) are <see cref="AutoMode.Region"/>; anything
    /// else is <see cref="AutoMode.Window"/>. The Explorer rules compare exactly, case and all;
    /// the shell hosts ignore ASCII case, as the regular expression's <c>/i</c> does.
    /// </summary>
    public static AutoMode Classify(string app, string title)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(title);
        if (app == "Windows Explorer" && title == "Program Manager") return AutoMode.Fullscreen;
        if (app == "Windows Explorer" && JsString.Trim(title).Length == 0) return AutoMode.Region;
        var lower = AsciiCase.Lower(app);
        foreach (var host in ShellHosts)
        {
            if (lower.Contains(host, StringComparison.Ordinal)) return AutoMode.Region;
        }
        return AutoMode.Window;
    }
}
