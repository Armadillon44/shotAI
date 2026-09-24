using System.Text.RegularExpressions;
using ShotAI.App.Tests.Support;
using Xunit;

namespace ShotAI.App.Tests.Threading;

/// <summary>
/// T6 and T9, spec 11 8.2 (AC-IPC-6): belt and braces for the analyzers, a scan of the App's
/// source, comments and string literals removed, for synchronous waits and direct dispatcher
/// calls outside the allowlisted files.
/// </summary>
/// <remarks>
/// A text scan rather than the Roslyn syntax scan spec 11 names, so the tests take no compiler
/// package: the analyzers already work on the syntax, and this check only has to notice a
/// suppression or a pattern they miss.
/// </remarks>
public sealed partial class NoSyncWaitTests
{
    // Each rule with the files that may break it (ARCHITECTURE 14.9).
    private static readonly (string Name, Regex Pattern, string[] Allowed)[] Rules =
    [
        ("Task.Wait", WaitCall(), ["Threading/ShutdownFlush.cs"]),
        (".Result", ResultRead(), []),
        ("GetAwaiter().GetResult()", GetResultCall(), []),
        ("Dispatcher.Invoke", DispatcherInvoke(), ["Threading/WpfUiDispatcher.cs", "Threading/UiDeferral.cs", "Editor/StaRenderThread.cs"]),
    ];

    [Fact]
    public void NoSynchronousWaitOutsideTheAllowlist()
    {
        var root = Path.Combine(RepoFiles.Root, "dotnet", "src", "ShotAI.App");
        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(f => !f.StartsWith("obj/", StringComparison.Ordinal) && !f.StartsWith("bin/", StringComparison.Ordinal))
            .ToList();
        Assert.Contains("App.xaml.cs", files);
        var hits = files.SelectMany(f => Scan(f, File.ReadAllText(Path.Combine(root, f)))).ToList();
        Assert.Empty(hits);
    }

    /// <summary>The allowlisted wait is where the scan expects it.</summary>
    [Fact]
    public void TheExitFlushIsTheOneWait()
    {
        var text = Code(RepoFiles.ReadText("dotnet/src/ShotAI.App/Threading/ShutdownFlush.cs"));
        Assert.Single(WaitCall().Matches(text));
    }

    /// <summary>Each rule fires on its pattern and not inside a comment or a string.</summary>
    [Theory]
    [InlineData("task.Wait();", "Task.Wait")]
    [InlineData("task.Wait(TimeSpan.FromSeconds(1));", "Task.Wait")]
    [InlineData("Task.WaitAll(a, b);", "Task.Wait")]
    [InlineData("Task.WaitAny(a, b);", "Task.Wait")]
    [InlineData("var x = task.Result;", ".Result")]
    [InlineData("task.GetAwaiter().GetResult();", "GetAwaiter().GetResult()")]
    [InlineData("task.ConfigureAwait(false).GetAwaiter().GetResult();", "GetAwaiter().GetResult()")]
    [InlineData("Dispatcher.Invoke(() => { });", "Dispatcher.Invoke")]
    [InlineData("Dispatcher.BeginInvoke(a);", "Dispatcher.Invoke")]
    [InlineData("_dispatcher.InvokeAsync(a);", "Dispatcher.Invoke")]
    public void RulesFire(string line, string rule) => Assert.Equal([$"X.cs: {rule}"], Scan("X.cs", "class X { void M() { " + line + " } }"));

    [Theory]
    [InlineData("// task.Wait();")]
    [InlineData("/* task.Result */")]
    [InlineData("var s = \"task.Wait()\";")]
    [InlineData("var s = @\"task.GetAwaiter().GetResult()\";")]
    [InlineData("await task;")]
    [InlineData("var r = e.ResultCode;")]
    [InlineData("WaitHandle.WaitOne();")]
    public void RulesIgnoreTheRest(string line) => Assert.Empty(Scan("X.cs", "class X { void M() { " + line + " } }"));

    [Fact]
    public void AllowlistedFilesMayBreakTheirRule()
    {
        Assert.Empty(Scan("Threading/ShutdownFlush.cs", "t.Wait(timeout);"));
        Assert.Equal(["Threading/ShutdownFlush.cs: .Result"], Scan("Threading/ShutdownFlush.cs", "t.Wait(timeout); var r = t.Result;"));
    }

    internal static List<string> Scan(string file, string source)
    {
        var code = Code(source);
        return Rules.Where(r => !r.Allowed.Contains(file) && r.Pattern.IsMatch(code)).Select(r => $"{file}: {r.Name}").ToList();
    }

    // The source with comments and string and character literals blanked, so only code is matched.
    internal static string Code(string source) => Noise().Replace(source, m => m.Value.StartsWith("//", StringComparison.Ordinal) ? "" : " ");

    [GeneratedRegex(@"//[^\n]*|/\*.*?\*/|@""(?:[^""]|"""")*""|\$?""(?:[^""\\\n]|\\.)*""|'(?:[^'\\\n]|\\.)*'", RegexOptions.Singleline)]
    private static partial Regex Noise();

    [GeneratedRegex(@"\.Wait(All|Any)?\s*\(")]
    private static partial Regex WaitCall();

    [GeneratedRegex(@"\.Result\b")]
    private static partial Regex ResultRead();

    [GeneratedRegex(@"GetAwaiter\s*\(\s*\)\s*\.\s*GetResult\s*\(")]
    private static partial Regex GetResultCall();

    [GeneratedRegex(@"[Dd]ispatcher\s*\.\s*(Begin)?Invoke(Async)?\s*\(")]
    private static partial Regex DispatcherInvoke();
}
