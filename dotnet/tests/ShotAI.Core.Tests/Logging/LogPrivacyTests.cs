using Microsoft.Extensions.Logging;
using ShotAI.Core.Settings;
using ShotAI.Core.Store;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Logging;

/// <summary>
/// INV-INFRA-21 (spec 10 7.5.5, ARCHITECTURE 8.5): canary credentials and personal content run
/// through every path that logs, with the real file provider at its lowest level, and no line of
/// <c>shotai.log</c> holds any of them.
/// </summary>
/// <remarks>
/// The settings and store paths exist now. The auth-status and SOP-request paths join this test
/// in the work packages that build them, and AC-INFRA-16 is met with all of them (WP-E6).
/// </remarks>
public sealed class LogPrivacyTests
{
    private const string ApiKey = "sk-ant-api03-CANARYKEY-0123456789";
    private const string Token = "sk-ant-oat01-CANARYTOKEN-0123456789";
    private const string UserName = "Canary Q. Username";
    private const string Title = "Canary Payroll Title";
    private const string Instructions = "Canary custom instructions";
    private const string Caption = "Canary caption text";

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task CanaryNeverLogged()
    {
        var ct = TestContext.Current.CancellationToken;
        using var h = new LogHarness();
        var provider = h.Provider(h.Options(minimum: LogLevel.Trace), startWriter: true);
        var paths = new TestAppPaths(h.Temp.Root);
        Assert.Equal(h.LogsDirectory, paths.LogsDirectory);
        Directory.CreateDirectory(paths.UserDataDirectory);
        var atomic = new AtomicFile(h.Time, new ManagedRenameRetryClassifier());
        SettingsService Load() => SettingsService.Load(paths, atomic, h.Time, provider.CreateLogger<SettingsService>());

        // Settings: unreadable, corrupt and not an object, each holding the canaries.
        Directory.CreateDirectory(paths.SettingsFile);
        await Load().DisposeAsync();
        Directory.Delete(paths.SettingsFile);
        await File.WriteAllTextAsync(paths.SettingsFile, $$"""{"userName":"{{UserName}}","apiKey":"{{ApiKey}}","token":"{{Token}}","sop":{"customInstructions":"{{Instructions}}"},""", ct);
        await Load().DisposeAsync();
        await File.WriteAllTextAsync(paths.SettingsFile, $"""["{ApiKey}","{Token}","{UserName}"]""", ct);
        await Load().DisposeAsync();

        // A settings object with the canaries in known and unknown keys and a relative projects
        // folder; two changes with canary values; then a hand edit that is not an object.
        await File.WriteAllTextAsync(
            paths.SettingsFile,
            $$$"""{"projectsDir":"{{{Title}}}/projects","userName":"{{{UserName}}}","apiKey":"{{{ApiKey}}}","oauth":{"access":"{{{Token}}}"},"sop":{"customInstructions":"{{{Instructions}}}","model":"{{{Title}}}"}}""",
            ct);
        var settings = Load();
        await settings.UpdateAsync(s => s with { UserName = UserName + " 2", Sop = s.Sop with { CustomInstructions = Instructions + " 2" } }, ct);
        await File.WriteAllTextAsync(paths.SettingsFile, $"\"{ApiKey}\"", ct);
        await settings.UpdateAsync(s => s with { HasSeenTour = true }, ct);

        // Store: a project with a canary title and a caption, a malformed step, a damaged
        // project.json full of canaries, a rename to another canary title, and an archive.
        var probe = new ManagedPathProbe();
        var archive = new ArchiveEngine(probe, atomic, provider.CreateLogger<ArchiveEngine>());
        await using (var store = new ProjectStore(settings, probe, atomic, archive, h.Time, provider.CreateLogger<ProjectStore>()))
        {
            var project = await store.CreateProjectAsync(Title);
            var manifest = await File.ReadAllTextAsync(Path.Combine(project.Path, "project.json"), ct);
            await File.WriteAllTextAsync(
                Path.Combine(project.Path, "project.json"),
                manifest.Replace("\"steps\": []", $$"""
                    "steps": [{"id":"s1","order":1,"caption":"{{Caption}}","notes":"{{UserName}}"}, "{{ApiKey}}"]
                    """, StringComparison.Ordinal),
                ct);
            var damaged = Path.Combine(settings.Current.ProjectsDir, "damaged");
            Directory.CreateDirectory(damaged);
            await File.WriteAllTextAsync(Path.Combine(damaged, "project.json"), $$"""{"title":"{{Title}}","steps":[{"caption":"{{Caption}}" x""", ct);
            await store.ListProjectsAsync(ct);
            await store.RenameProjectAsync(project.Path, Title + " renamed");
            await store.ArchiveProjectAsync(project.Path);
            await store.FlushAsync(Wait);
        }
        await settings.FlushAsync(Wait);
        await settings.DisposeAsync();
        Assert.True(provider.Flush(Wait));

        var text = h.Text();
        Assert.NotNull(text);
        foreach (var canary in new[] { ApiKey, Token, UserName, Title, Instructions, Caption, "sk-ant", "canary" })
            Assert.DoesNotContain(canary, text, StringComparison.OrdinalIgnoreCase);

        // Each path above did log, so the test is not passing on an empty file.
        Assert.Contains("(projects) settings.json unreadable, using defaults: ", text, StringComparison.Ordinal);
        Assert.Contains("(projects) settings.json is not a settings object, using defaults", text, StringComparison.Ordinal);
        Assert.Contains("(projects) settings: projectsDir is not an absolute path, using the default", text, StringComparison.Ordinal);
        Assert.Contains("(projects) settings.json is not a settings object, writing the saved settings over it", text, StringComparison.Ordinal);
        Assert.Contains("(projects) manifest: dropped 1 malformed step(s) of 2", text, StringComparison.Ordinal);
        Assert.Contains("(projects) list: skipped damaged, not a readable project", text, StringComparison.Ordinal);
        Assert.Contains("(projects) archive: packed 0 file(s) \u2192 ", text, StringComparison.Ordinal);
    }
}
