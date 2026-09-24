using ShotAI.App.Shell;

namespace ShotAI.App.Tests.Support;

/// <summary>An <see cref="IMainWindowLayout"/> that records every call.</summary>
internal sealed class RecordingLayout : IMainWindowLayout
{
    /// <summary>The calls, in order.</summary>
    public List<(bool Open, double Scale)> Calls { get; } = [];

    public void SetDetailView(bool open, double scale) => Calls.Add((open, scale));
}
