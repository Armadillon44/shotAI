using System.Text.RegularExpressions;
using ShotAI.Core.Logging;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Logging;

/// <summary>Spec 10 7.5.3: the category of a logger picks its label by namespace.</summary>
public sealed partial class LogCategoriesTests
{
    /// <summary>
    /// Rows use namespaces of ARCHITECTURE 2.4 only; a prefix matches a whole namespace, so
    /// <c>ShotAI.Core.SettingsUi</c> is not under <c>ShotAI.Core.Settings</c>.
    /// </summary>
    [Theory]
    [InlineData("ShotAI.Core.Store.ArchiveEngine", "projects")]
    [InlineData("ShotAI.Core.Store.ProjectStore", "projects")]
    [InlineData("ShotAI.Core.Settings.SettingsService", "projects")]
    [InlineData("ShotAI.Core.Settings", "projects")]
    [InlineData("ShotAI.Core.Capture.CaptureEngine", "capture")]
    [InlineData("ShotAI.Platform.Capture.UiaElementLocator", "capture")]
    [InlineData("ShotAI.Core.Sop.ClaudeService", "claude")]
    [InlineData("ShotAI.Core.Sop.Transport.Anything", "claude")]
    [InlineData("ShotAI.Core.Auth.AuthService", "claude")]
    [InlineData("ShotAI.Platform.Auth.MsalGateway", "claude")]
    [InlineData("ShotAI.Platform.Ocr.WindowsOcrEngine", "ocr")]
    [InlineData("ShotAI.Core.Redaction.RenderGate", "ocr")]
    [InlineData("ShotAI.Core.Threading.EventRaiser", "svc")]
    [InlineData("ShotAI.Core.Links.ExternalLinks", "svc")]
    [InlineData("ShotAI.Core.Updates.UpdateService", "main")]
    [InlineData("ShotAI.Core.Export.ExportEngine", "main")]
    [InlineData("ShotAI.Core.Rendering.Flattener", "main")]
    [InlineData("ShotAI.App.Home.HomeViewModel", "main")]
    [InlineData("ShotAI.App", "main")]
    [InlineData("ShotAI.Core.SettingsUi.SettingsText", "main")]
    [InlineData("ShotAI.Core.Storex.Anything", "main")]
    [InlineData("ShotAI.Core.Store2", "main")]
    [InlineData("shotai.core.store.ProjectStore", "main")]
    [InlineData("ShotAI.Core", "main")]
    [InlineData("Microsoft.Extensions.Hosting.Internal.Host", "main")]
    [InlineData("System.Net.Http.HttpClient", "main")]
    [InlineData("", "main")]
    public void MapsByNamespace(string category, string label) => Assert.Equal(label, LogCategories.LabelFor(category));

    /// <summary>An explicit category equal to a label keeps it (<c>CreateLogger("claude")</c>); the comparison is ordinal.</summary>
    [Theory]
    [InlineData("main", "main")]
    [InlineData("projects", "projects")]
    [InlineData("capture", "capture")]
    [InlineData("claude", "claude")]
    [InlineData("ocr", "ocr")]
    [InlineData("svc", "svc")]
    [InlineData("Projects", "main")]
    [InlineData("SVC", "main")]
    [InlineData("ipc", "main")]
    [InlineData(" claude", "main")]
    public void ALabelIsItsOwnCategory(string category, string label) => Assert.Equal(label, LogCategories.LabelFor(category));

    /// <summary>The reserved banner category, and only it, has the empty label.</summary>
    [Fact]
    public void BannerHasTheEmptyLabel()
    {
        Assert.Equal("ShotAI.Banner", LogCategories.Banner);
        Assert.Equal("", LogCategories.LabelFor(LogCategories.Banner));
        Assert.Equal("main", LogCategories.LabelFor("ShotAI.Banner.More"));
        Assert.Equal("main", LogCategories.LabelFor("shotai.banner"));
    }

    [Fact]
    public void NullIsRefused() => Assert.Throws<ArgumentNullException>(() => LogCategories.LabelFor(null!));

    /// <summary>No label is longer than <c>projects</c>, so the fixed width 11 holds for every line.</summary>
    [Fact]
    public void LabelsAtMost8Chars()
    {
        Assert.Equal(8, LogCategories.MaxLabelLength);
        Assert.Equal(LogCategories.MaxLabelLength + 3, FileLogLineFormatter.ScopeWidth);
        Assert.All(LogCategories.Labels, label => Assert.InRange(label.Length, 1, LogCategories.MaxLabelLength));
        Assert.Equal(LogCategories.MaxLabelLength, LogCategories.Labels.Max(l => l.Length));
        Assert.All(LogCategories.Labels, label => Assert.Equal(FileLogLineFormatter.ScopeWidth, FileLogLineFormatter.ScopeText(label).Length));
    }

    /// <summary>The table of 7.5.3, row for row.</summary>
    [Fact]
    public void TableIsSpec10_7_5_3()
    {
        Assert.Equal(["main", "projects", "capture", "claude", "ocr", "svc"], LogCategories.Labels);
        Assert.Equal(
            [
                ("ShotAI.Core.Store", "projects"),
                ("ShotAI.Core.Settings", "projects"),
                ("ShotAI.Core.Capture", "capture"),
                ("ShotAI.Platform.Capture", "capture"),
                ("ShotAI.Core.Sop", "claude"),
                ("ShotAI.Core.Auth", "claude"),
                ("ShotAI.Platform.Auth", "claude"),
                ("ShotAI.Platform.Ocr", "ocr"),
                ("ShotAI.Core.Redaction", "ocr"),
                ("ShotAI.Core.Threading", "svc"),
                ("ShotAI.Core.Links", "svc"),
            ],
            LogCategories.Prefixes.Select(p => (p.Key, p.Value)));
        Assert.All(LogCategories.Prefixes, p => Assert.Contains(p.Value, LogCategories.Labels));
    }

    /// <summary>Every prefix of the table is a namespace ARCHITECTURE 2.4 lists, so none is a typo that maps nothing.</summary>
    [Fact]
    public void EveryPrefixIsANamespaceOfArchitecture2_4()
    {
        var namespaces = ArchitectureNamespaces();
        Assert.Contains("ShotAI.Core.Store", namespaces);
        Assert.Contains("ShotAI.Core.SettingsUi", namespaces);
        Assert.Contains("ShotAI.Platform.Ocr", namespaces);
        Assert.All(LogCategories.Prefixes, p => Assert.Contains(p.Key, namespaces));
    }

    /// <summary>The labels are Electron's six scopes, with <c>svc</c> for <c>ipc</c> (11 7.11 L1).</summary>
    [Fact]
    public void LabelsAreElectronScopesWithSvcForIpc()
    {
        var logger = ElectronSource.Read("src/main/logger.ts");
        var scopes = ScopeCall().Matches(logger).Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(["main", "capture", "ipc", "projects", "claude", "ocr"], scopes);
        Assert.Equal(
            scopes.Select(s => s == "ipc" ? "svc" : s).Order(StringComparer.Ordinal),
            LogCategories.Labels.Order(StringComparer.Ordinal));
    }

    // The namespace column of ARCHITECTURE 2.4, with ".X" read against its project's root.
    private static HashSet<string> ArchitectureNamespaces()
    {
        var text = RepoFiles.ReadText("docs/native/ARCHITECTURE.md");
        var start = text.IndexOf("### 2.4 Namespaces and folders", StringComparison.Ordinal);
        Assert.True(start >= 0, "ARCHITECTURE.md has no section 2.4");
        var end = text.IndexOf("\n### ", start + 1, StringComparison.Ordinal);
        var namespaces = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in text[start..end].Split('\n'))
        {
            var cells = row.Split('|');
            if (cells.Length < 4) continue;
            var project = cells[1].Trim();
            if (project is not ("Core" or "Platform" or "App")) continue;
            foreach (Match m in Backticked().Matches(cells[2]))
            {
                var name = m.Groups[1].Value;
                namespaces.Add(name.StartsWith('.') ? "ShotAI." + project + name : name);
            }
        }
        return namespaces;
    }

    [GeneratedRegex(@"log\.scope\('([a-z]+)'\)")]
    private static partial Regex ScopeCall();

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex Backticked();
}
