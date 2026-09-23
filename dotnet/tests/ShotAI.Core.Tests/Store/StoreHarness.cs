using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using ShotAI.Core.Json;
using ShotAI.Core.Store;
using ShotAI.Core.Tests.Support;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// A real <see cref="ProjectStore"/> over a temp projects folder, as Electron's store tests run
/// the real store against a temp directory with only the settings boundary faked.
/// </summary>
internal sealed class StoreHarness : IAsyncDisposable
{
    /// <summary>The manifest the Electron store tests start from.</summary>
    public const string BaseJson =
        """{"version":1,"id":"test","title":"T","createdWith":"shotAI","createdAt":"2026-01-01T00:00:00.000Z","updatedAt":"2026-01-01T00:00:00.000Z","captureSettings":null,"steps":[],"sopBackup":null}""";

    /// <summary>The fixed "now" of every harness clock.</summary>
    public static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 34, 56, 789, TimeSpan.Zero);

    /// <param name="brand">The app brand the settings report.</param>
    /// <param name="newId">The source of new project ids; a fresh UUID each time when null.</param>
    public StoreHarness(string brand = "shotAI", Func<string>? newId = null)
    {
        Directory.CreateDirectory(Root);
        Settings = new FakeProjectStoreSettings(Root) { Brand = brand };
        var probe = new ManagedPathProbe();
        var atomic = new AtomicFile(Time, new ManagedRenameRetryClassifier());
        var log = Logs.CreateLogger<ProjectStore>();
        Store = newId is null
            ? new ProjectStore(Settings, probe, atomic, Time, log)
            : new ProjectStore(Settings, probe, atomic, Time, log, newId);
    }

    public TempDir Temp { get; } = new("store-");

    public string Root => Temp.Combine("projects");

    public FakeTimeProvider Time { get; } = new(Now);

    public CapturingLoggerProvider Logs { get; } = new();

    public FakeProjectStoreSettings Settings { get; }

    public ProjectStore Store { get; }

    /// <summary>A project folder under the root holding <paramref name="json"/> as its manifest.</summary>
    public string Project(string name, string json = BaseJson)
    {
        var dir = Path.Combine(Root, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "project.json"), json);
        return dir;
    }

    /// <summary>The manifest exactly as it sits on disk.</summary>
    public static string Bytes(string dir) => File.ReadAllText(Path.Combine(dir, "project.json"));

    /// <summary>The manifest on disk, parsed as <c>JSON.parse</c> would.</summary>
    public static JsonObject OnDisk(string dir) => JsJson.Parse(Bytes(dir))!.AsObject();

    /// <summary>
    /// Waits for the queued writes, at most 10 s of real time: a test that fails while a job it
    /// blocked is still waiting then fails with a timeout instead of hanging the run.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await Store.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System);
        }
        finally
        {
            Logs.Dispose();
            Temp.Dispose();
        }
    }
}
