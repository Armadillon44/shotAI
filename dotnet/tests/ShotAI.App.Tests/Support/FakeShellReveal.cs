using ShotAI.Core.Shell;

namespace ShotAI.App.Tests.Support;

/// <summary>An <see cref="IShellReveal"/> that records each project reveal and can fail it or hold it open; it opens no Explorer window.</summary>
internal sealed class FakeShellReveal : IShellReveal
{
    private TaskCompletionSource? _gate;

    /// <summary>Each project path revealed, in order.</summary>
    public List<string> Revealed { get; } = [];

    /// <summary>When set, a reveal fails with it.</summary>
    public Exception? Failure { get; set; }

    /// <summary>The next reveal waits for the returned source, which the test completes.</summary>
    public TaskCompletionSource Gate() => _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task RevealProjectAsync(string projectPath, CancellationToken ct = default)
    {
        Revealed.Add(projectPath);
        if (_gate is { } gate)
        {
            _gate = null;
            await gate.Task;
        }
        if (Failure is { } failure) throw failure;
    }

    public Task RevealInExplorerAsync(string path) => throw new NotSupportedException();

    public Task OpenFolderAsync(string directory) => throw new NotSupportedException();
}
