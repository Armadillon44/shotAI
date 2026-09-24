using ShotAI.App.Tests.Support;
using ShotAI.Core.Store;
using ShotAI.Platform;
using Xunit;

namespace ShotAI.App.Tests.Architecture;

/// <summary>
/// INV-ARCH-5 and INV-IPC-20 (spec 11 8.2, AC-SHELL-24): WebView2 is Platform's alone, in
/// <c>ShotAI.Platform.Export</c>, and the App has no reference to it. Landed with the package, in
/// WP-A15, for the version probe; the PDF host (WP-D10) is its second user there.
/// </summary>
public sealed class SingleWebViewTests
{
    private const string WebView2 = "Microsoft.Web.WebView2";

    [Fact]
    public void OnlyPlatformReferencesTheWebView2Core()
    {
        Assert.Contains(typeof(DllSearchHardening).Assembly.GetReferencedAssemblies(), a => a.Name == WebView2 + ".Core");
        Assert.DoesNotContain(typeof(App).Assembly.GetReferencedAssemblies(), a => a.Name!.StartsWith(WebView2, StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(IProjectService).Assembly.GetReferencedAssemblies(), a => a.Name!.StartsWith(WebView2, StringComparison.Ordinal));
    }

    /// <summary>In Platform's source only the <c>Export</c> folder, <c>ShotAI.Platform.Export</c>, names WebView2; nothing else does.</summary>
    [Fact]
    public void OnlyPlatformExportNamesWebView2()
    {
        var src = Path.Combine(RepoFiles.Root, "dotnet", "src");
        var naming = Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".xaml", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(src, f).Replace('\\', '/'))
            .Where(f => !f.Contains("/obj/", StringComparison.Ordinal) && !f.Contains("/bin/", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(Path.Combine(src, f)).Contains(WebView2, StringComparison.Ordinal))
            .ToList();
        Assert.Contains("ShotAI.Platform/Export/WebView2RuntimeInfo.cs", naming);
        Assert.All(naming, f => Assert.StartsWith("ShotAI.Platform/Export/", f, StringComparison.Ordinal));
    }

    /// <summary>The App gets the package only through Platform: its project names no WebView2 package.</summary>
    [Fact]
    public void TheAppHasNoWebView2PackageReference() =>
        Assert.DoesNotContain(WebView2, RepoFiles.ReadText("dotnet/src/ShotAI.App/ShotAI.App.csproj"), StringComparison.Ordinal);

    /// <summary>The package's WPF and WinForms controls are dropped from every project (Directory.Build.targets), so neither ships.</summary>
    [Fact]
    public void TheControlAssembliesAreNotInTheOutput()
    {
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, WebView2 + ".Core.dll")), "the core assembly the probe needs is missing");
        Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, WebView2 + ".Wpf.dll")));
        Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, WebView2 + ".WinForms.dll")));
    }
}
