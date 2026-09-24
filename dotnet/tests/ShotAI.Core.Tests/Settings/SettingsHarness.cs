using System.Text;
using System.Text.Json.Nodes;
using ShotAI.Core.Json;
using ShotAI.Core.Settings;
using ShotAI.Core.Store;
using ShotAI.Core.Tests.Support;

namespace ShotAI.Core.Tests.Settings;

/// <summary>
/// The real <see cref="SettingsService"/> over a temp user-data folder, the way Electron's
/// settings test runs the real module against a temp directory. Timers run on a fake clock the
/// test advances; <see cref="DisposeAsync"/> waits at most 10 s of real time for queued writes.
/// </summary>
internal sealed class SettingsHarness : IAsyncDisposable
{
    private readonly List<SettingsService> _services = [];

    public SettingsHarness()
    {
        Paths = new TestAppPaths(Temp.Root);
        Directory.CreateDirectory(Paths.UserDataDirectory);
    }

    public TempDir Temp { get; } = new("settings-");

    public TestAppPaths Paths { get; }

    public string File => Paths.SettingsFile;

    public string DefaultDir => Paths.DefaultProjectsDir;

    public TimerLog Time { get; } = new();

    public CapturingLoggerProvider Logs { get; } = new();

    /// <summary>Loads the service as startup does, over the file as it is now.</summary>
    public SettingsService Load(IRenameRetryClassifier? classifier = null)
    {
        var service = SettingsService.Load(Paths, new AtomicFile(Time, classifier ?? new ManagedRenameRetryClassifier()), Time, Logs.CreateLogger<SettingsService>());
        _services.Add(service);
        return service;
    }

    /// <summary>Writes the settings file as UTF-8 without a BOM.</summary>
    public void Write(string text) => System.IO.File.WriteAllText(File, text, new UTF8Encoding(false));

    /// <summary>The file exactly as it sits on disk.</summary>
    public string Text() => System.IO.File.ReadAllText(File, new UTF8Encoding(false));

    /// <summary>The file, parsed as <c>JSON.parse</c> would.</summary>
    public JsonObject OnDisk() => JsJson.Parse(System.IO.File.ReadAllBytes(File))!.AsObject();

    /// <summary>The log lines the service wrote, in order.</summary>
    public string[] Lines() => [.. Logs.Entries.Select(e => e.Message)];

    public async ValueTask DisposeAsync()
    {
        try
        {
            foreach (var service in _services)
                await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System);
        }
        finally
        {
            Logs.Dispose();
            Temp.Dispose();
        }
    }
}
