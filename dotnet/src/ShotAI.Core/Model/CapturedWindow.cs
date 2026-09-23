using System.Text.Json.Nodes;

namespace ShotAI.Core.Model;

/// <summary>The foreground window of a capture (spec 01 2.4), as capture writes it.</summary>
/// <param name="App">The executable name, for example <c>chrome.exe</c>.</param>
public sealed record CapturedWindow(string App, string Title, double Pid, Rect? Bounds)
{
    /// <summary>The stored form: <c>app, title, pid, bounds</c> (<c>bounds</c> may be null).</summary>
    public JsonObject ToJson() => new()
    {
        ["app"] = App,
        ["title"] = Title,
        ["pid"] = Pid,
        ["bounds"] = Bounds?.ToJson(),
    };
}
