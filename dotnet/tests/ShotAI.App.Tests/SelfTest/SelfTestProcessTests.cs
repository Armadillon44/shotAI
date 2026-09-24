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
[Collection(AppProcessCollection.Name)]
public sealed partial class SelfTestProcessTests
{
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
        var before = Hash(AppProcess.Settings);
        var run = await RunAsync(["--selftest"], temp.Root);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(before, Hash(AppProcess.Settings));
        Assert.Empty(Directory.GetFileSystemEntries(temp.Root));
    }

    /// <summary>
    /// The run's own lines in the user's log: the banner of AC-INFRA-14, <c>logs:</c> with the
    /// log's path, every self-test line under <c>main</c>, and <c>exiting (code 0)</c> last.
    /// </summary>
    [Fact]
    public async Task LogsTheBannerTheLinesAndTheExit()
    {
        var start = AppProcess.LogLength();
        using var temp = new TempDir();
        var run = await RunAsync(["--selftest"], temp.Root);
        Assert.Equal(0, run.ExitCode);

        var lines = AppProcess.LogFrom(start);
        var banner = lines.FindLastIndex(l => l.Contains("shotAI starting", StringComparison.Ordinal));
        Assert.True(banner >= 0, "no banner in the log");
        Assert.Matches(Banner(), lines[banner]);
        Assert.EndsWith("] [info]             logs: " + AppProcess.Log, lines[banner + 1], StringComparison.OrdinalIgnoreCase);
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
        var psi = AppProcess.StartInfo(args, temp, env);
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.StandardOutputEncoding = Encoding.UTF8;
        psi.StandardErrorEncoding = Encoding.UTF8;
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        var exitCode = await AppProcess.WaitForExitAsync(process);
        return (exitCode, await output, await error);
    }

    private static string LastLine(string text) => text.TrimEnd().Split('\n')[^1].TrimEnd('\r');

    private static string? Hash(string file) => File.Exists(file) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))) : null;

    // AC-INFRA-14, with the real U+2014 and U+00B7.
    [GeneratedRegex("^\\[\\d{4}-\\d{2}-\\d{2} \\d{2}:\\d{2}:\\d{2}\\.\\d{3}\\] \\[info\\] {13}shotAI starting \u2014 win32/(x64|arm64) \u00b7 .+ \u00b7 packaged=(true|false)$")]
    private static partial Regex Banner();
}
