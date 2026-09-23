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

    /// <summary>The 8-byte PNG signature: the smallest buffer the importer accepts as a PNG.</summary>
    public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <param name="brand">The app brand the settings report.</param>
    /// <param name="newId">The source of new project ids; a fresh UUID each time when null.</param>
    public StoreHarness(string brand = "shotAI", Func<string>? newId = null)
    {
        Directory.CreateDirectory(Root);
        Settings = new FakeProjectStoreSettings(Root) { Brand = brand };
        var probe = new ManagedPathProbe();
        var atomic = new AtomicFile(Time, new ManagedRenameRetryClassifier());
        Archive = new ArchiveEngine(probe, atomic, Logs.CreateLogger<ArchiveEngine>());
        var log = Logs.CreateLogger<ProjectStore>();
        Store = newId is null
            ? new ProjectStore(Settings, probe, atomic, Archive, Time, log)
            : new ProjectStore(Settings, probe, atomic, Archive, Time, log, newId);
    }

    public TempDir Temp { get; } = new("store-");

    public string Root => Temp.Combine("projects");

    public FakeTimeProvider Time { get; } = new(Now);

    public CapturingLoggerProvider Logs { get; } = new();

    public FakeProjectStoreSettings Settings { get; }

    public ProjectStore Store { get; }

    /// <summary>The engine the store archives with, for tests that drive it directly.</summary>
    public ArchiveEngine Archive { get; }

    /// <summary>A project folder under the root holding <paramref name="json"/> as its manifest.</summary>
    public string Project(string name, string json = BaseJson)
    {
        var dir = Path.Combine(Root, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "project.json"), json);
        return dir;
    }

    /// <summary><see cref="BaseJson"/> with <paramref name="steps"/>, a JSON array, as its steps.</summary>
    public static string WithSteps(string steps) => BaseJson.Replace("\"steps\":[]", "\"steps\":" + steps, StringComparison.Ordinal);

    /// <summary>Writes <paramref name="bytes"/> (a PNG signature by default) at <paramref name="rel"/> inside <paramref name="dir"/>.</summary>
    public static string WriteFile(string dir, string rel, byte[]? bytes = null)
    {
        var path = Path.Join(dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes ?? Png);
        return path;
    }

    /// <summary>The ids of the steps on disk, in order; "" for a step whose id is not a string.</summary>
    public static string[] StepIds(string dir) =>
        OnDisk(dir)["steps"]!.AsArray().Select(s => JsValue.TryGetString(s!["id"], out var id) ? id : "").ToArray();

    /// <summary>The <c>order</c> of each step on disk.</summary>
    public static double[] StepOrders(string dir) => OnDisk(dir)["steps"]!.AsArray().Select(s => s!["order"]!.GetValue<double>()).ToArray();

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
