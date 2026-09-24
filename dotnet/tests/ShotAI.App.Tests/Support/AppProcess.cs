using System.Diagnostics;
using System.Text;
using Xunit;

namespace ShotAI.App.Tests.Support;

/// <summary>
/// The built <c>shotAI.exe</c> run as a child process, as a user starts it. The child runs as
/// the user, so it writes the user's own log and holds the user's own single-instance lock; the
/// tests that run it share <see cref="AppProcessCollection"/>.
/// </summary>
internal static class AppProcess
{
    /// <summary>A bound on each wait for the child, well above what a CI runner needs.</summary>
    public static readonly TimeSpan Bound = TimeSpan.FromSeconds(90);

    /// <summary><c>%APPDATA%\shotAI</c>.</summary>
    public static string UserData { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "shotAI");

    /// <summary>The user's <c>shotai.log</c>.</summary>
    public static string Log => Path.Combine(UserData, "logs", "shotai.log");

    /// <summary>The user's <c>settings.json</c>.</summary>
    public static string Settings => Path.Combine(UserData, "settings.json");

    /// <summary>
    /// A start of the exe with <paramref name="args"/>, its temp folder <paramref name="temp"/>,
    /// no self-test variable of the test's own environment, and <paramref name="env"/> on top.
    /// </summary>
    public static ProcessStartInfo StartInfo(IEnumerable<string> args, string temp, params (string Name, string Value)[] env)
    {
        var psi = new ProcessStartInfo(Exe()) { UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["TEMP"] = temp;
        psi.Environment["TMP"] = temp;
        psi.Environment.Remove("SHOTAI_SELFTEST");
        psi.Environment.Remove("SHOTAI_CAPTURE_TEST");
        psi.Environment.Remove("SHOTAI_LOG_LEVEL");
        foreach (var (name, value) in env) psi.Environment[name] = value;
        return psi;
    }

    /// <summary>Waits for the child to exit, killing it and failing when it does not within <see cref="Bound"/>.</summary>
    public static async Task<int> WaitForExitAsync(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Bound);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        return process.ExitCode;
    }

    /// <summary>
    /// The child's main window once it is visible: the first visible top-level window it owns,
    /// which is what <see cref="Process.MainWindowHandle"/> finds.
    /// </summary>
    public static async Task<nint> MainWindowAsync(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < Bound)
        {
            if (process.HasExited) Assert.Fail($"shotAI exited with code {process.ExitCode} before it showed a window");
            process.Refresh();
            var window = process.MainWindowHandle;
            if (window != 0 && User32.IsWindowVisible(window)) return window;
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
        Assert.Fail("shotAI showed no window");
        return 0;
    }

    /// <summary>
    /// Waits until the child has logged its first render (startup step 12), so every startup step
    /// has run, the activation listener of step 11 included; returns the child's main window.
    /// </summary>
    public static async Task<nint> StartedAsync(Process process, long logStart)
    {
        var window = await MainWindowAsync(process);
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < Bound)
        {
            if (process.HasExited) Assert.Fail($"shotAI exited with code {process.ExitCode} during startup");
            if (LogFrom(logStart).Any(l => l.Contains("startup: main window rendered in ", StringComparison.Ordinal))) return window;
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
        Assert.Fail("shotAI did not log its first render");
        return 0;
    }

    /// <summary>The length of the user's log now, where a run's lines will start.</summary>
    public static long LogLength() => File.Exists(Log) ? new FileInfo(Log).Length : 0;

    /// <summary>The lines appended to the user's log since it was <paramref name="start"/> bytes long, or the whole file after a rotation.</summary>
    public static List<string> LogFrom(long start)
    {
        using var stream = new FileStream(Log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length >= start) stream.Position = start;
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        return reader.ReadToEnd().Split("\r\n", StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    // The App's own output, which the solution build puts beside this project's with the same
    // configuration and target framework.
    private static string Exe()
    {
        var here = new DirectoryInfo(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        var exe = Path.Combine(RepoFiles.Root, "dotnet", "src", "ShotAI.App", "bin", here.Parent!.Name, here.Name, "shotAI.exe");
        Assert.True(File.Exists(exe), $"build the App first: {exe} does not exist");
        return exe;
    }
}

/// <summary>
/// The tests that run the real exe share the user's log and single-instance lock, so they run
/// one at a time and never beside another test of this assembly.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AppProcessCollection
{
    public const string Name = "app process";
}
