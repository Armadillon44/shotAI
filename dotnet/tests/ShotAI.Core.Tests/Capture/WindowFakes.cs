using ShotAI.Core.Capture;
using ShotAI.Core.Model;

namespace ShotAI.Core.Tests.Capture;

/// <summary>A window as <see cref="FakeWindowFacts"/> reports it; every field has a default that describes and lists.</summary>
internal sealed class FakeWindow
{
    public int Pid { get; set; } = 100;

    public string Title { get; set; } = "Untitled";

    public Rect? WindowRect { get; set; } = new Rect(10, 20, 300, 200);

    public bool HasClientRect { get; set; } = true;

    public Rect? Frame { get; set; } = new Rect(17, 20, 286, 193);

    public bool Minimized { get; set; }

    public WindowListFacts List { get; set; } = new(true, true, true, WindowListFilter.WsCaption, 0, false, false);

    public List<nint> Children { get; } = [];
}

/// <summary>A scripted <see cref="IWindowFacts"/> that records what it was asked.</summary>
internal sealed class FakeWindowFacts : IWindowFacts
{
    public nint ForegroundWindow { get; set; }

    /// <summary>The top-level windows, top of the z order first.</summary>
    public List<nint> ZOrder { get; } = [];

    public Dictionary<nint, FakeWindow> Windows { get; } = [];

    /// <summary>Each process's image path; a missing pid, or null, does not open.</summary>
    public Dictionary<int, string?> Images { get; } = [];

    public Dictionary<string, string> Descriptions { get; } = new(StringComparer.Ordinal);

    public List<int> ImageQueries { get; } = [];

    public List<string> DescriptionReads { get; } = [];

    public int DescendantQueries { get; private set; }

    /// <summary>Adds a top-level window below the others and returns it.</summary>
    public FakeWindow Add(nint hwnd, int pid = 100, string title = "Untitled")
    {
        var window = new FakeWindow { Pid = pid, Title = title };
        Windows[hwnd] = window;
        ZOrder.Add(hwnd);
        return window;
    }

    /// <summary>Adds a process with its image and, when given, its FileDescription.</summary>
    public void Process(int pid, string? image, string? description = null)
    {
        Images[pid] = image;
        if (image is not null && description is not null) Descriptions[image] = description;
    }

    public nint Foreground() => ForegroundWindow;

    public IReadOnlyList<nint> TopLevelWindows() => [.. ZOrder];

    public IReadOnlyList<nint> Descendants(nint hwnd)
    {
        DescendantQueries++;
        return Windows.TryGetValue(hwnd, out var w) ? [.. w.Children] : [];
    }

    public WindowListFacts ListFacts(nint hwnd) => Windows.TryGetValue(hwnd, out var w) ? w.List : default;

    public int ProcessId(nint hwnd) => Windows.TryGetValue(hwnd, out var w) ? w.Pid : 0;

    public string? ImagePath(int pid)
    {
        ImageQueries.Add(pid);
        return Images.GetValueOrDefault(pid);
    }

    public string FileDescription(string path)
    {
        DescriptionReads.Add(path);
        return Descriptions.GetValueOrDefault(path, "");
    }

    public Rect? WindowRect(nint hwnd) => Windows[hwnd].WindowRect;

    public bool HasClientRect(nint hwnd) => Windows[hwnd].HasClientRect;

    public Rect? FrameBounds(nint hwnd) => Windows[hwnd].Frame;

    public string Title(nint hwnd) => Windows[hwnd].Title;

    public bool IsMinimized(nint hwnd) => Windows[hwnd].Minimized;
}
