using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using ShotAI.Core.Capture;
using ShotAI.Core.Tests.SourceGuards;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>
/// Spec 02 8.2 and 8.4, INV-CAP-1 and risk R2: the routing invariant, asserted from the
/// source and by reflection. With remote visibility on, the pill is capturable for the whole
/// recording, so a single screen read outside the funnel puts it into a saved screenshot, and
/// nothing fails or logs. The scans cover every C# file under <c>dotnet/src</c> and
/// <c>dotnet/tools</c>; the allowlists below are the reviewed exceptions. Each rule is also run
/// on a changed copy, to prove it can fail.
/// </summary>
public sealed partial class CaptureFunnelSourceTests
{
    private const string FunnelPath = "src/ShotAI.Core/Capture/ShieldedScreenCapture.cs";
    private const string SeamsPath = "src/ShotAI.Core/Capture/CaptureSeams.cs";
    private const string GdiPath = "src/ShotAI.Platform/Capture/GdiMonitorCapture.cs";
    private const string PlatformRegistrationPath = "src/ShotAI.Platform/Capture/PlatformCaptureRegistration.cs";

    // The protection probe measures the exclusion itself, so it must read raw pixels (02 8.4).
    private const string ProbePrefix = "tools/ShotAI.ProtectionProbe/";

    // The Win32 and .NET calls that read another window's pixels: GDI's copies from a screen
    // DC, PrintWindow, WinForms' CopyFromScreen and DXGI desktop duplication.
    private static readonly string[] RawReadApis = ["BitBlt", "StretchBlt", "PrintWindow", "CopyFromScreen", "DuplicateOutput"];

    // Who may name the raw capture: its declaration, the funnel, its implementation (WP-B5) and
    // the one Platform registration file that binds the two, which AddShotAIPlatform calls (02 7.1).
    private static readonly string[] MonitorCaptureNamers = [SeamsPath, FunnelPath, GdiPath, PlatformRegistrationPath];

    // Who may name the implementation, which is internal to Platform (INV-ARCH-4).
    private static readonly string[] GdiNamers = [GdiPath, PlatformRegistrationPath];

    [Fact]
    public void ShieldedCaptureUsesUsingScope() =>
        Assert.Null(FunnelProblem(File.ReadAllText(Full(FunnelPath))));

    /// <summary>A bare take leaks the shield when the read throws, and the app stays invisible to the remote viewer.</summary>
    [Theory]
    [InlineData("using var _ = _shield.Take();", "var _ = _shield.Take();")]
    [InlineData("using var _ = _shield.Take();", "")]
    [InlineData("using var _ = _shield.Take();\n        return _raw.Capture(monitor);", "var frame = _raw.Capture(monitor);\n        using var _ = _shield.Take();\n        return frame;")]
    [InlineData("    public MonitorDescriptor? FromPoint(int x, int y)", "    public PixelFrame Peek(MonitorDescriptor m) => _raw.Capture(m);\n\n    public MonitorDescriptor? FromPoint(int x, int y)")]
    [InlineData("_shield.Take()", "_shield.ToString()")]
    public void AFunnelThatReadsOutsideTheShieldFails(string from, string to)
    {
        var source = File.ReadAllText(Full(FunnelPath)).ReplaceLineEndings("\n");
        Assert.Contains(from, source, StringComparison.Ordinal);
        Assert.NotNull(FunnelProblem(source.Replace(from, to, StringComparison.Ordinal)));
    }

    [Fact]
    public void OnlyFunnelReadsScreenPixels()
    {
        var files = SourceFiles();
        Assert.Empty(RawReadProblems(files));
        // The scan is not vacuous: it sees the funnel, and the funnel names the raw capture.
        Assert.Contains(files, f => f.Path == FunnelPath && Names(f.Code, "IMonitorCapture"));
    }

    /// <summary>
    /// The mutation of AC-CAP-3 as a unit case: a direct read in the engine is reported, as is
    /// a raw API outside the Platform capture, a holder of the implementation, and a second
    /// raw read in a file allowed to name the interface.
    /// </summary>
    [Theory]
    [InlineData("src/ShotAI.Core/Capture/CaptureEngine.cs", "internal sealed class CaptureEngine(IMonitorCapture raw) { public object Read(MonitorDescriptor m) => raw.Capture(m); }")]
    [InlineData("src/ShotAI.App/Capture/PillWindow.cs", "sealed class Pill { void Snap() => PInvoke.BitBlt(dst, 0, 0, 1, 1, src, 0, 0, ROP_CODE.SRCCOPY); }")]
    [InlineData("src/ShotAI.Platform/Capture/MenuPoller.cs", "sealed class MenuPoller { readonly GdiMonitorCapture _gdi = new(); }")]
    [InlineData("src/ShotAI.Platform/Imaging/Thumbs.cs", "sealed class Thumbs { void Take(System.Drawing.Graphics g) => g.CopyFromScreen(0, 0, 0, 0, default); }")]
    [InlineData(PlatformRegistrationPath, "static class X { static object Grab(IMonitorCapture c, MonitorDescriptor m) => c.Capture(m); }")]
    public void AnUnshieldedReadIsReported(string path, string code) =>
        Assert.NotEmpty(RawReadProblems([new CodeFile(path, CSharpText.StripComments(code))]));

    [Theory]
    [InlineData(GdiPath, "internal sealed class GdiMonitorCapture : IMonitorCapture { public PixelFrame Capture(MonitorDescriptor m) { PInvoke.BitBlt(dst, 0, 0, 1, 1, src, 0, 0, ROP_CODE.SRCCOPY); return null!; } }")]
    [InlineData(PlatformRegistrationPath, "static class X { static void Add(IServiceCollection s) => s.AddSingleton<IMonitorCapture, GdiMonitorCapture>(); }")]
    [InlineData("tools/ShotAI.ProtectionProbe/Program.cs", "PInvoke.BitBlt(dst, 0, 0, 1, 1, src, 0, 0, ROP_CODE.SRCCOPY);")]
    [InlineData("src/ShotAI.Core/Capture/CaptureEngine.cs", "// the engine never calls BitBlt or IMonitorCapture.Capture directly")]
    public void TheReviewedExceptionsAreAllowed(string path, string code) =>
        Assert.Empty(RawReadProblems([new CodeFile(path, CSharpText.StripComments(code))]));

    /// <summary>
    /// Nothing in Core but the funnel holds the raw capture: no constructor, field, property or
    /// method of any other type takes, stores or returns an <see cref="IMonitorCapture"/>. This
    /// is 8.2's <c>EngineDependsOnlyOnShieldedCapture</c> for every type, so it covers
    /// <c>CaptureEngine</c> the moment it exists.
    /// </summary>
    [Fact]
    public void OnlyTheFunnelHoldsTheRawCapture() =>
        Assert.Empty(RawCaptureHolders(typeof(ShieldedScreenCapture).Assembly));

    [Fact]
    public void TheHolderCheckFindsAHolder()
    {
        var holders = RawCaptureHolders(typeof(CaptureFunnelSourceTests).Assembly);
        Assert.Contains(holders, h => h.StartsWith(typeof(RawHolder).FullName!, StringComparison.Ordinal));
        Assert.Contains(holders, h => h.StartsWith(typeof(RawFactory).FullName!, StringComparison.Ordinal));
    }

    /// <summary>
    /// No <c>Windows.Graphics.Capture</c> anywhere in the product's sources or tools (ARCHITECTURE
    /// 9.2 S6): its session draws a border and its exclusion rules are not the ones the shield
    /// was built on. Every file is read, not only C#, since a CsWin32 list or a project file
    /// would bring it in.
    /// </summary>
    [Fact]
    public void NoGraphicsCaptureUsage()
    {
        var offenders = new List<string>();
        foreach (var path in AllFiles())
        {
            if (GraphicsCaptureProblem(File.ReadAllBytes(Full(path))) is { } problem) offenders.Add(path + ": " + problem);
        }
        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData("using Windows.Graphics.Capture;")]
    [InlineData("IGraphicsCaptureItemInterop")]
    [InlineData("var item = GraphicsCaptureItem.TryCreateFromWindowId(id);")]
    public void AGraphicsCaptureReferenceIsReported(string text) =>
        Assert.NotNull(GraphicsCaptureProblem(Encoding.UTF8.GetBytes(text)));

    /// <summary>Null when the funnel's only raw read is inside <c>using var _ = _shield.Take();</c> in <c>Grab</c>.</summary>
    internal static string? FunnelProblem(string source)
    {
        var code = CSharpText.StripComments(source);
        var reads = RawCaptureCall().Matches(code).Count;
        if (reads != 1) return $"the funnel makes {reads} raw reads; it must make exactly one, in Grab";
        var grab = GrabSignature().Match(code);
        if (!grab.Success) return "the funnel has no Grab(MonitorDescriptor) method";
        var open = grab.Index + grab.Length - 1;
        var close = MatchingBrace(code, open);
        if (close < 0) return "Grab's body does not close";
        var body = code[(open + 1)..close];
        var take = UsingTake().Match(body);
        if (!take.Success) return "Grab does not hold _shield.Take() in a using declaration";
        return RawCaptureCall().IsMatch(body[(take.Index + take.Length)..])
            ? null
            : "Grab's raw read is not inside the shield's using scope";
    }

    /// <summary>Every raw pixel read, or name of the raw capture, outside its allowlist.</summary>
    internal static List<string> RawReadProblems(IEnumerable<CodeFile> files)
    {
        var problems = new List<string>();
        foreach (var f in files)
        {
            var probe = f.Path.StartsWith(ProbePrefix, StringComparison.Ordinal);
            foreach (var api in RawReadApis)
            {
                if (!probe && f.Path != GdiPath && Regex.IsMatch(f.Code, @"\b" + api + @"\s*\("))
                    problems.Add($"{f.Path}: calls {api}; only GdiMonitorCapture reads the screen");
            }
            if (!probe && !MonitorCaptureNamers.Contains(f.Path) && Names(f.Code, "IMonitorCapture"))
                problems.Add($"{f.Path}: names IMonitorCapture; take IScreenCapture, the shielded funnel");
            if (!probe && !GdiNamers.Contains(f.Path) && Names(f.Code, "GdiMonitorCapture"))
                problems.Add($"{f.Path}: names GdiMonitorCapture, which only the registration may bind");
            if (!probe && f.Path != FunnelPath && MonitorCaptureNamers.Contains(f.Path) && RawCaptureCall().IsMatch(f.Code))
                problems.Add($"{f.Path}: calls .Capture( on the raw capture; only ShieldedScreenCapture may");
        }
        return problems;
    }

    /// <summary>Each member of a type other than the funnel that takes, stores or returns an <see cref="IMonitorCapture"/>.</summary>
    internal static List<string> RawCaptureHolders(Assembly assembly)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var holders = new List<string>();
        foreach (var type in assembly.GetTypes())
        {
            if (type == typeof(IMonitorCapture) || IsWithin(type, typeof(ShieldedScreenCapture))) continue;
            foreach (var c in type.GetConstructors(all))
            {
                if (c.GetParameters().Any(p => Mentions(p.ParameterType))) holders.Add($"{type.FullName}: constructor");
            }
            foreach (var fi in type.GetFields(all))
            {
                if (Mentions(fi.FieldType)) holders.Add($"{type.FullName}: field {fi.Name}");
            }
            foreach (var pi in type.GetProperties(all))
            {
                if (Mentions(pi.PropertyType)) holders.Add($"{type.FullName}: property {pi.Name}");
            }
            foreach (var m in type.GetMethods(all))
            {
                if (Mentions(m.ReturnType) || m.GetParameters().Any(p => Mentions(p.ParameterType))) holders.Add($"{type.FullName}: method {m.Name}");
            }
        }
        return holders;
    }

    /// <summary>Null when the bytes hold no Windows.Graphics.Capture name.</summary>
    internal static string? GraphicsCaptureProblem(ReadOnlySpan<byte> bytes)
    {
        foreach (var token in new[] { "Windows.Graphics.Capture", "GraphicsCapture" })
        {
            if (bytes.IndexOf(Encoding.ASCII.GetBytes(token)) >= 0) return "mentions " + token;
        }
        return null;
    }

    private static bool Mentions(Type t)
    {
        if (t == typeof(IMonitorCapture)) return true;
        if (t.HasElementType) return Mentions(t.GetElementType()!);
        return t.IsGenericType && t.GetGenericArguments().Any(Mentions);
    }

    private static bool IsWithin(Type type, Type outer)
    {
        for (var t = type; t is not null; t = t.DeclaringType)
        {
            if (t == outer) return true;
        }
        return false;
    }

    private static bool Names(string code, string identifier) => Regex.IsMatch(code, @"\b" + identifier + @"\b");

    private static int MatchingBrace(string code, int open)
    {
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}' && --depth == 0) return i;
        }
        return -1;
    }

    private static string Full(string path) => Path.Combine(RepoFiles.Root, "dotnet", path);

    // The C# files under dotnet/src and dotnet/tools, comments blanked, by their path under
    // dotnet/ with forward slashes. Build output is not source.
    private static List<CodeFile> SourceFiles() =>
        AllFiles()
            .Where(p => p.EndsWith(".cs", StringComparison.Ordinal))
            .Select(p => new CodeFile(p, CSharpText.StripComments(File.ReadAllText(Full(p)))))
            .ToList();

    private static IEnumerable<string> AllFiles()
    {
        var dotnet = Path.Combine(RepoFiles.Root, "dotnet");
        foreach (var top in new[] { "src", "tools" })
        {
            var root = Path.Combine(dotnet, top);
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var rel = Path.GetRelativePath(dotnet, file).Replace('\\', '/');
                var segments = rel.Split('/');
                if (segments.Contains("bin") || segments.Contains("obj")) continue;
                yield return rel;
            }
        }
    }

    // A test double of the kind the reflection must catch; tests may hold the raw capture.
    private sealed class RawHolder(IMonitorCapture raw)
    {
        public PixelFrame Read(MonitorDescriptor m) => raw.Capture(m);
    }

    private static class RawFactory
    {
        public static IMonitorCapture? Make() => null;
    }

    [GeneratedRegex(@"\.\s*Capture\s*\(")]
    private static partial Regex RawCaptureCall();

    [GeneratedRegex(@"public\s+PixelFrame\s+Grab\s*\(\s*MonitorDescriptor\s+\w+\s*\)\s*\{")]
    private static partial Regex GrabSignature();

    [GeneratedRegex(@"\busing\s+var\s+\w+\s*=\s*_shield\s*\.\s*Take\s*\(\s*\)\s*;")]
    private static partial Regex UsingTake();
}

/// <summary>A C# file with its comments blanked, by its path under <c>dotnet/</c>.</summary>
internal sealed record CodeFile(string Path, string Code);
