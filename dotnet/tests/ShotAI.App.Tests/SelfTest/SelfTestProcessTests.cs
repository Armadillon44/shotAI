using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ShotAI.App.Tests.Support;
using Xunit;

namespace ShotAI.App.Tests.SelfTest;

/// <summary>
/// Spec 10 8.5 (AC-INFRA-25, AC-INFRA-27, and AC-INFRA-14 for the run's own lines): the built
/// <c>shotAI.exe</c> run with <c>--selftest</c> and its output redirected, as the documented
/// <c>Start-Process</c> call runs it. <c>--update-selftest</c> is not run (network).
/// </summary>
/// <remarks>
/// The child runs as the user, so it writes the user's own log, as the manual procedure does;
/// the tests give it a temp folder of its own through <c>TEMP</c> and <c>TMP</c>.
/// </remarks>
[Collection(SelfTestProcessCollection.Name)]
public sealed partial class SelfTestProcessTests
{
    private static readonly string UserData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "shotAI");

    [Fact]
    public async Task SwitchPassesAndExitsZero()
    {
        using var temp = new TempDir();
        var run = await RunAsync(["--selftest"], temp.Root);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("[selftest] PASS", LastLine(run.Output));
        Assert.Equal("", run.Error);
        Assert.StartsWith("[selftest] projectsDir  = " + Path.Combine(temp.Root, "shotai-selftest-"), run.Output, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Electron's variable still works, with no switch.</summary>
    [Fact]
    public async Task VariablePassesToo()
    {
        using var temp = new TempDir();
        var run = await RunAsync([], temp.Root, ("SHOTAI_SELFTEST", "1"));
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("[selftest] PASS", LastLine(run.Output));
    }

    /// <summary>AC-INFRA-27: the user's settings file is byte-identical, and nothing is left in the temp folder.</summary>
    [Fact]
    public async Task LeavesTheUserSettingsAndTempClean()
    {
        using var temp = new TempDir();
        var settings = Path.Combine(UserData, "settings.json");
        var before = Hash(settings);
        var run = await RunAsync(["--selftest"], temp.Root);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(before, Hash(settings));
        Assert.Empty(Directory.GetFileSystemEntries(temp.Root));
    }

    /// <summary>
    /// The run's own lines in the user's log: the banner of AC-INFRA-14, <c>logs:</c> with the
    /// log's path, every self-test line under <c>main</c>, and <c>exiting (code 0)</c> last.
    /// </summary>
    [Fact]
    public async Task LogsTheBannerTheLinesAndTheExit()
    {
        var log = Path.Combine(UserData, "logs", "shotai.log");
        var start = File.Exists(log) ? new FileInfo(log).Length : 0;
        using var temp = new TempDir();
        var run = await RunAsync(["--selftest"], temp.Root);
        Assert.Equal(0, run.ExitCode);

        var lines = ReadFrom(log, start);
        var banner = lines.FindLastIndex(l => l.Contains("shotAI starting", StringComparison.Ordinal));
        Assert.True(banner >= 0, "no banner in the log");
        Assert.Matches(Banner(), lines[banner]);
        Assert.EndsWith("] [info]             logs: " + log, lines[banner + 1], StringComparison.OrdinalIgnoreCase);
        Assert.Contains(lines.Skip(banner), l => l.EndsWith("] [info]  (main)     [selftest] PASS", StringComparison.Ordinal));
        Assert.EndsWith("] [info]  (main)     exiting (code 0)", lines[^1], StringComparison.Ordinal);
    }

    /// <summary>The capture self-test is spec 02's (WP-B9); until then its switch fails with exit code 2.</summary>
    [Fact]
    public async Task CaptureSwitchIsAnErrorInThisBuild()
    {
        using var temp = new TempDir();
        var run = await RunAsync(["--capture-selftest"], temp.Root);
        Assert.Equal(2, run.ExitCode);
        Assert.Equal("[capture-test] ERROR this build has no capture self-test", run.Error.TrimEnd());
        Assert.Equal("", run.Output);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(string[] args, string temp, params (string Name, string Value)[] env)
    {
        var psi = new ProcessStartInfo(ShotAIExe())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["TEMP"] = temp;
        psi.Environment["TMP"] = temp;
        psi.Environment.Remove("SHOTAI_SELFTEST");
        psi.Environment.Remove("SHOTAI_CAPTURE_TEST");
        foreach (var (name, value) in env) psi.Environment[name] = value;

        using var process = Process.Start(psi)!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        return (process.ExitCode, await output, await error);
    }

    // The App's own output, which the solution build puts beside this project's with the same
    // configuration and target framework.
    private static string ShotAIExe()
    {
        var here = new DirectoryInfo(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        var exe = Path.Combine(RepoFiles.Root, "dotnet", "src", "ShotAI.App", "bin", here.Parent!.Name, here.Name, "shotAI.exe");
        Assert.True(File.Exists(exe), $"build the App first: {exe} does not exist");
        return exe;
    }

    private static string LastLine(string text) => text.TrimEnd().Split('\n')[^1].TrimEnd('\r');

    private static string? Hash(string file) => File.Exists(file) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))) : null;

    // The lines appended since the log was start bytes long, or the whole file after a rotation.
    private static List<string> ReadFrom(string file, long start)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length >= start) stream.Position = start;
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        return reader.ReadToEnd().Split("\r\n", StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    // AC-INFRA-14, with the real U+2014 and U+00B7.
    [GeneratedRegex("^\\[\\d{4}-\\d{2}-\\d{2} \\d{2}:\\d{2}:\\d{2}\\.\\d{3}\\] \\[info\\] {13}shotAI starting \u2014 win32/(x64|arm64) \u00b7 .+ \u00b7 packaged=(true|false)$")]
    private static partial Regex Banner();
}

/// <summary>The runs share the user's log file, so they run one at a time.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SelfTestProcessCollection
{
    public const string Name = "self-test process";
}
