using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.Core.Store;
using ShotAI.Platform.FileSystem;
using ShotAI.Platform.Shell;
using ShotAI.Platform.Tests.Support;
using Xunit;

namespace ShotAI.Platform.Tests.Shell;

/// <summary>
/// Spec 11 8.2 and 7.3.3 (D-IPC-6, ARCHITECTURE S17): a project reveal passes the store's
/// known-project gate first; every shell call runs on its own STA thread, never the caller's; a
/// folder open goes through the shell's open verb, and only for an existing directory. The
/// shell calls are recorded, so no Explorer window opens.
/// </summary>
/// <remarks>
/// In this project, not the App's that spec 11's table names: the shell seam is internal to
/// Platform, whose internals only this project sees.
/// </remarks>
public sealed class ShellRevealTests : IAsyncLifetime
{
    private static readonly WindowsPathProbe Probe = new();
    private static readonly AtomicFile Atomic = new(TimeProvider.System, new WindowsRenameRetryClassifier());

    private readonly TempDir _temp = new("shell-reveal-");
    private readonly ProjectStore _store;
    private readonly RecordingShell _shell = new();

    public ShellRevealTests()
    {
        _store = new ProjectStore(
            new FakeProjectStoreSettings(_temp.Combine("projects")),
            Probe,
            Atomic,
            new ArchiveEngine(Probe, Atomic, NullLogger<ArchiveEngine>.Instance),
            TimeProvider.System,
            NullLogger<ProjectStore>.Instance);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _store.DisposeAsync();
        _temp.Dispose();
    }

    /// <summary>A path outside the projects folder and the recents is refused with the gate's message, and the shell is never called.</summary>
    [Fact]
    public async Task RevealProjectRejectsUnknownProject()
    {
        var reveal = new ShellReveal(_store, _shell);
        var outside = Directory.CreateDirectory(_temp.Combine("elsewhere")).FullName;
        var e = await Assert.ThrowsAsync<ProjectNotKnownException>(() => reveal.RevealProjectAsync(outside, TestContext.Current.CancellationToken));
        Assert.Equal("Project path is not within the projects directory", e.Message);
        Assert.Empty(_shell.Calls);
    }

    /// <summary>A known project is revealed at the path the gate resolved.</summary>
    [Fact]
    public async Task RevealProjectRevealsTheResolvedPath()
    {
        var project = await _store.CreateProjectAsync("Handbook");
        var reveal = new ShellReveal(_store, _shell);
        await reveal.RevealProjectAsync(project.Path, TestContext.Current.CancellationToken);
        var call = Assert.Single(_shell.Calls);
        Assert.Equal(("select", await _store.ResolveKnownProjectAsync(project.Path)), (call.Kind, call.Path));
    }

    /// <summary>The shell call runs on a new background STA thread, not the caller's, even when the caller is an STA thread itself.</summary>
    [Fact]
    public async Task RevealRunsOnStaThread()
    {
        var reveal = new ShellReveal(_store, _shell);
        var caller = await OnStaThreadAsync(() => reveal.RevealInExplorerAsync(@"C:\Projects\Handbook"));
        var call = Assert.Single(_shell.Calls);
        Assert.Equal(ApartmentState.STA, call.Apartment);
        Assert.True(call.Background);
        Assert.Equal(StaThread.Name, call.Thread.Name);
        Assert.NotSame(caller, call.Thread);
        Assert.NotSame(Thread.CurrentThread, call.Thread);
    }

    /// <summary>A folder opens through the shell's open verb on an STA thread (S17).</summary>
    [Fact]
    public async Task OpenFolderUsesShellExecute()
    {
        var folder = Directory.CreateDirectory(_temp.Combine("export")).FullName;
        await new ShellReveal(_store, _shell).OpenFolderAsync(folder);
        var call = Assert.Single(_shell.Calls);
        Assert.Equal("start", call.Kind);
        Assert.Equal(ApartmentState.STA, call.Apartment);
        Assert.NotNull(call.Info);
        Assert.Equal((folder, true, "open"), (call.Info.FileName, call.Info.UseShellExecute, call.Info.Verb));
    }

    /// <summary>A file, or a path that is gone, opens nothing: the shell's open would run the file (S17).</summary>
    [Fact]
    public async Task OpenFolderNeverLaunchesAFile()
    {
        var reveal = new ShellReveal(_store, _shell);
        await reveal.OpenFolderAsync(_temp.File("export.html", "<p>x</p>"));
        await reveal.OpenFolderAsync(_temp.Combine("gone"));
        Assert.Empty(_shell.Calls);
    }

    /// <summary>What the shell call throws faults the task, for the caller's notice.</summary>
    [Fact]
    public async Task AShellFailureFaultsTheTask()
    {
        _shell.Failure = ShellReveal.ShellFailure(unchecked((int)0x80070035));
        var e = await Assert.ThrowsAsync<IOException>(() => new ShellReveal(_store, _shell).RevealInExplorerAsync(@"\\offline\share\Handbook"));
        Assert.Same(_shell.Failure, e);
    }

    /// <summary>A failed HRESULT that carries a Win32 error reads as the system's text for that error; another keeps the runtime's text.</summary>
    [Fact]
    public void ShellFailureCarriesTheSystemMessage()
    {
        var badPath = ShellReveal.ShellFailure(unchecked((int)0x80070035));
        Assert.Equal(unchecked((int)0x80070035), badPath.HResult);
        Assert.Equal(new System.ComponentModel.Win32Exception(0x35).Message, badPath.Message);
        Assert.False(string.IsNullOrWhiteSpace(badPath.Message));

        var noInterface = ShellReveal.ShellFailure(unchecked((int)0x80004002));
        Assert.Equal(unchecked((int)0x80004002), noInterface.HResult);
        Assert.False(string.IsNullOrWhiteSpace(noInterface.Message));
    }

    [Fact]
    public async Task ArgumentsAreChecked()
    {
        Assert.Throws<ArgumentNullException>(() => new ShellReveal(null!, _shell));
        Assert.Throws<ArgumentNullException>(() => new ShellReveal(_store, null!));
        var reveal = new ShellReveal(_store, _shell);
        await Assert.ThrowsAsync<ArgumentNullException>(() => reveal.RevealProjectAsync(null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => reveal.RevealInExplorerAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => reveal.OpenFolderAsync(null!));
    }

    // Runs start on a new STA thread, as the UI thread is, and returns that thread once the task it started completes.
    private static async Task<Thread> OnStaThreadAsync(Func<Task> start)
    {
        Task? started = null;
        var thread = new Thread(() => started = start());
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        await started!;
        return thread;
    }

    private sealed record ShellCall(string Kind, string Path, ProcessStartInfo? Info, ApartmentState Apartment, bool Background, Thread Thread);

    private sealed class RecordingShell : IShellCalls
    {
        public List<ShellCall> Calls { get; } = [];

        public Exception? Failure { get; set; }

        public void OpenFolderAndSelect(string path) => Record("select", path, null);

        public void Start(ProcessStartInfo info) => Record("start", info.FileName, info);

        private void Record(string kind, string path, ProcessStartInfo? info)
        {
            var t = Thread.CurrentThread;
            lock (Calls) Calls.Add(new ShellCall(kind, path, info, t.GetApartmentState(), t.IsBackground, t));
            if (Failure is { } failure) throw failure;
        }
    }
}
