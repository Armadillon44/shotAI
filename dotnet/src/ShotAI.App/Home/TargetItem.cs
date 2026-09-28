using ShotAI.Core.Capture;
using ShotAI.Core.Home;

namespace ShotAI.App.Home;

/// <summary>
/// One row of the target dropdown's list (spec 06 2.4): a window, its app first when it has one,
/// or a monitor, its size after its name. <see cref="IsPicked"/> is the <c>--on</c> row.
/// </summary>
public sealed class TargetItem
{
    private TargetItem(WindowInfo? window, MonitorInfo? monitor, string name, string detail, bool detailFirst, bool isPicked)
    {
        Window = window;
        Monitor = monitor;
        Name = name;
        Detail = detail;
        DetailFirst = detailFirst;
        IsPicked = isPicked;
    }

    /// <summary>The listed window, or null for a monitor.</summary>
    public WindowInfo? Window { get; }

    /// <summary>The listed monitor, or null for a window.</summary>
    public MonitorInfo? Monitor { get; }

    /// <summary>The row's main text: the window's title, or the monitor's name.</summary>
    public string Name { get; }

    /// <summary>The row's secondary text: the window's app, empty when it has none, or the monitor's size.</summary>
    public string Detail { get; }

    /// <summary>The detail comes before the name, as a window's app does.</summary>
    public bool DetailFirst { get; }

    /// <summary>The detail shows: a window has an app, or it is a monitor.</summary>
    public bool HasDetail => Detail.Length > 0;

    /// <summary>The picked row.</summary>
    public bool IsPicked { get; }

    /// <summary>What a screen reader reads: the row's text in the order it is shown.</summary>
    public string AccessibleName => !HasDetail ? Name : DetailFirst ? Detail + " " + Name : Name + " " + Detail;

    /// <summary>A window's row.</summary>
    public static TargetItem For(WindowInfo window, bool picked)
    {
        ArgumentNullException.ThrowIfNull(window);
        return new TargetItem(window, null, HomeText.WindowItemName(window), window.App, detailFirst: true, picked);
    }

    /// <summary>A monitor's row.</summary>
    public static TargetItem For(MonitorInfo monitor, bool picked)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        return new TargetItem(null, monitor, monitor.Name, HomeText.MonitorItemDetail(monitor), detailFirst: false, picked);
    }
}
