using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Json;
using ShotAI.Core.Paths;
using ShotAI.Core.Store;

namespace ShotAI.Core.SelfTest;

/// <summary>
/// The store self-test, <c>--selftest</c> (spec 10 2.9 and 7.8): Electron's <c>runSelfTest</c>
/// (<c>src/main/selftest.ts</c>) against a store and settings of its own, so the user's
/// <c>settings.json</c> is never opened (IMPROVEMENT, INV-INFRA-30, EDGE-INFRA-35).
/// </summary>
public static partial class StoreSelfTest
{
    /// <summary>The title of the first two projects: the same display name twice.</summary>
    public const string Title = "Self Test Project";

    /// <summary>A title with characters no folder name can hold; the folder is still a UUID.</summary>
    public const string SpecialTitle = "Flow: A/B - C*";

    // How long step 7 waits for the store's and the settings' queued writes: the exit flush's budget.
    private static readonly TimeSpan DisposeBound = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Runs the test and returns its outcome, which is the process exit code. Each line goes to
    /// <paramref name="output"/>, or to <paramref name="error"/> for the <c>ERROR</c> line, and to
    /// <paramref name="log"/> at Information.
    /// </summary>
    /// <remarks>
    /// The test folder is <c>&lt;TempDirectory&gt;\shotai-selftest-&lt;pid&gt;</c> and its settings
    /// file the sibling <c>shotai-selftest-&lt;pid&gt;.settings.json</c>. Both are removed at the
    /// end, errors ignored; the clean-up never changes the outcome, and it waits at most 5 s for
    /// the queued writes, so it cannot hold up the exit.
    /// </remarks>
    /// <param name="factory">Builds the isolated settings service and store.</param>
    /// <param name="paths">The app's paths; only <see cref="IAppPaths.TempDirectory"/> is used as it is.</param>
    /// <param name="output">Standard output.</param>
    /// <param name="error">Standard error.</param>
    /// <param name="log">The self-test's logger, under <c>main</c>.</param>
    public static Task<SelfTestOutcome> RunAsync(ProjectStoreFactory factory, IAppPaths paths, TextWriter output, TextWriter error, ILogger log) =>
        RunAsync(factory, paths, output, error, log, TimeProvider.System);

    /// <summary><see cref="RunAsync(ProjectStoreFactory, IAppPaths, TextWriter, TextWriter, ILogger)"/> with the clock of the clean-up's bound given.</summary>
    internal static async Task<SelfTestOutcome> RunAsync(
        ProjectStoreFactory factory, IAppPaths paths, TextWriter output, TextWriter error, ILogger log, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(time);
        var testRoot = Path.Combine(paths.TempDirectory, "shotai-selftest-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        var settingsFile = testRoot + ".settings.json";
        SelfTestStore? store = null;
        try
        {
            store = factory.Create(new IsolatedPaths(paths, testRoot, settingsFile));
            return await RunStepsAsync(store.Projects, testRoot, output, log).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            await PrintAsync(error, log, "[selftest] ERROR " + e).ConfigureAwait(false);
            return SelfTestOutcome.Error;
        }
        finally
        {
            await CleanUpAsync(store, testRoot, settingsFile, time).ConfigureAwait(false);
        }
    }

    // selftest.ts:28-66, line for line.
    private static async Task<SelfTestOutcome> RunStepsAsync(IProjectService projects, string testRoot, TextWriter output, ILogger log)
    {
        await projects.SetProjectsDirAsync(testRoot).ConfigureAwait(false);
        // The value read back, not testRoot (selftest.ts:29).
        var projectsDir = await projects.GetProjectsDirAsync().ConfigureAwait(false);
        await PrintAsync(output, log, "[selftest] projectsDir  = " + projectsDir).ConfigureAwait(false);

        var created = await projects.CreateProjectAsync(Title).ConfigureAwait(false);
        var manifest = JsJson.Parse(await File.ReadAllBytesAsync(Path.Combine(created.Path, "project.json")).ConfigureAwait(false));
        var shots = Directory.Exists(Path.Combine(created.Path, "shots"));
        var exp = Directory.Exists(Path.Combine(created.Path, "export"));
        var created2 = await projects.CreateProjectAsync(Title).ConfigureAwait(false);
        var special = await projects.CreateProjectAsync(SpecialTitle).ConfigureAwait(false);
        var specialFolder = BaseName(special.Path);
        var recents = await projects.ListRecentProjectsAsync().ConfigureAwait(false);

        await PrintAsync(output, log, "[selftest] created       = " + created.Path).ConfigureAwait(false);
        await PrintAsync(output, log, ManifestLine(manifest)).ConfigureAwait(false);
        await PrintAsync(output, log, "[selftest] folder name   = " + Quote(BaseName(created.Path))).ConfigureAwait(false);
        await PrintAsync(output, log, "[selftest] shots/export  = " + Bool(shots) + " " + Bool(exp)).ConfigureAwait(false);
        await PrintAsync(output, log, "[selftest] unique folder = " + Bool(created.Path != created2.Path)).ConfigureAwait(false);
        await PrintAsync(output, log, "[selftest] special title = " + Quote(special.Title) + " folder " + Quote(specialFolder)).ConfigureAwait(false);
        await PrintAsync(output, log, "[selftest] recents       = " + recents.Count.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);

        var m = (JsonObject)manifest!;
        var ok = JsValue.TryGetNumber(m["version"], out var version) && version == 1
            && JsValue.TryGetString(m["createdWith"], out var createdWith) && createdWith == "shotAI"
            && JsValue.TryGetString(m["title"], out var title) && title == Title
            && IsUuid(BaseName(created.Path))
            && shots
            && exp
            && created.Path != created2.Path
            && special.Title == SpecialTitle
            && IsUuid(specialFolder)
            && recents.Count >= 3;
        await PrintAsync(output, log, ok ? "[selftest] PASS" : "[selftest] FAIL").ConfigureAwait(false);
        return ok ? SelfTestOutcome.Pass : SelfTestOutcome.Fail;
    }

    // util.format('[selftest] manifest      = v%d "%s" steps=%d', manifest.version, manifest.title,
    // manifest.steps.length). Reading steps.length of anything but an array is an error here, as
    // it is in JavaScript for a missing one; the store never writes anything else.
    private static string ManifestLine(JsonNode? manifest)
    {
        if (manifest is not JsonObject m || m["steps"] is not JsonArray steps)
            throw new InvalidDataException("project.json is not an object with a steps array");
        return "[selftest] manifest      = v" + FormatNumber(m["version"]) + " \"" + FormatString(m, "title") + "\" steps="
            + steps.Count.ToString(CultureInfo.InvariantCulture);
    }

    // %d: a number as JavaScript prints it (util.format keeps the sign of -0); anything else NaN.
    private static string FormatNumber(JsonNode? node)
    {
        if (!JsValue.TryGetNumber(node, out var d)) return "NaN";
        return d == 0 && double.IsNegative(d) ? "-0" : JsNumber.ToJsString(d);
    }

    // %s: a string as it is, a missing property as undefined, anything else as its JSON text.
    private static string FormatString(JsonObject m, string key)
    {
        if (!m.TryGetPropertyValue(key, out var node)) return "undefined";
        return JsValue.TryGetString(node, out var s) ? s : JsJson.Stringify(node, indent: 0);
    }

    // JSON.stringify of a string.
    private static string Quote(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        JsQuote.Append(sb, value);
        return sb.ToString();
    }

    private static string Bool(bool value) => value ? "true" : "false";

    // path.basename: the last segment, ignoring a trailing separator.
    private static string BaseName(string path) => Path.GetFileName(Path.TrimEndingDirectorySeparator(path));

    private static async Task PrintAsync(TextWriter writer, ILogger log, string line)
    {
        await writer.WriteLineAsync(line).ConfigureAwait(false);
        SelfTestLine(log, line);
    }

    // Step 7. Nothing here changes the outcome or holds up the exit for long.
    private static async Task CleanUpAsync(SelfTestStore? store, string testRoot, string settingsFile, TimeProvider time)
    {
        if (store is not null)
        {
            try
            {
                // The bound starts before the disposal does.
                using var bound = new CancellationTokenSource(DisposeBound, time);
                await store.DisposeAsync().AsTask().WaitAsync(bound.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // A write that failed or did not finish in time; the delete below may then leave files.
            }
        }
        try
        {
            ReparseSafeDelete.DeleteTree(testRoot, new ManagedPathProbe());
        }
        catch (Exception)
        {
            // Left for the temp folder's own clean-up.
        }
        try
        {
            File.Delete(settingsFile);
        }
        catch (Exception)
        {
            // Same.
        }
    }

    /// <summary><c>UUID_RE</c>, with ASCII hex digits and <c>\z</c> (EDGE-INFRA-43).</summary>
    internal static bool IsUuid(string name) => UuidFolder().IsMatch(name);

    [GeneratedRegex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\\z")]
    private static partial Regex UuidFolder();

    [LoggerMessage(Level = LogLevel.Information, Message = "{Line}")]
    private static partial void SelfTestLine(ILogger logger, string line);

    // The app's paths, with the two the settings service and the store read pointed at the test's own.
    private sealed class IsolatedPaths(IAppPaths paths, string testRoot, string settingsFile) : IAppPaths
    {
        public string UserDataDirectory => paths.UserDataDirectory;

        public string SettingsFile => settingsFile;

        public string LogsDirectory => paths.LogsDirectory;

        public string LocalDataDirectory => paths.LocalDataDirectory;

        public string DefaultProjectsDir => testRoot;

        public string TempDirectory => paths.TempDirectory;

        public string FontsDirectory => paths.FontsDirectory;
    }
}
