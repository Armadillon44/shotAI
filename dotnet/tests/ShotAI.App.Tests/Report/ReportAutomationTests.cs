using System.Windows.Automation.Peers;
using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.App.Report;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Store;
using Xunit;
using static ShotAI.App.Tests.Support.Manifests;

namespace ShotAI.App.Tests.Report;

/// <summary>
/// Spec 05 7.18, the read-only report's part: the cards are a List named by the project's
/// title, each row a ListItem named by its number and first line, a callout by its kind, a
/// section by its heading. The badge, grip, image controls and menus join with WP-C2 and WP-C3.
/// </summary>
public sealed class ReportAutomationTests
{
    [Fact]
    public Task TheReportIsAListOfNamedRows() => Sta.RunAsync(async () =>
    {
        var projects = new ListingProjects();
        var sessions = new FakeSessions();
        using var project = new ProjectDetailViewModel(projects, sessions, new ReportViewModelFactory(), new RecordingLayout(), new FixedTargets(), NullLogger<ProjectDetailViewModel>.Instance);
        var view = new ProjectDetailView { DataContext = project };
        var window = TestShell.Host(view);
        window.Show();
        try
        {
            projects.CanOpen(@"C:\Projects\A", Of(
                "Expense report",
                Shot("s1", caption: "Click Save"),
                Shot("s2"),
                Text("n1", "Heads up", callout: "caution"),
                Text("x1", "Part two", callout: "section"),
                Text("x2", callout: "section"),
                Text("t1", body: "Then close the window."),
                Text("t2", "Tip", callout: "tip")));
            Assert.True(await project.OpenAsync(@"C:\Projects\A"));
            await TestShell.Settle();

            var list = UIElementAutomationPeer.CreatePeerForElement(view.CardList);
            Assert.IsType<ReportListAutomationPeer>(list);
            Assert.Equal((AutomationControlType.List, "Expense report"), (list.GetAutomationControlType(), list.GetName()));
            var rows = list.GetChildren();
            Assert.All(rows, row => Assert.Equal(AutomationControlType.ListItem, row.GetAutomationControlType()));
            Assert.Equal(
                ["Step 1, Click Save", "Step 2", "Caution callout", "Part two", "Section", "Step 3, Then close the window.", "Step 4, Tip"],
                rows.Select(row => row.GetName()));

            // The names follow the steps.
            sessions.Created[0].Raise(Of("Expense report", Shot("s1", caption: "Click Open")), ManifestChangeKind.External);
            await TestShell.Settle();
            list.ResetChildrenCache();
            Assert.Equal(["Step 1, Click Open"], list.GetChildren().Select(row => row.GetName()));
        }
        finally
        {
            window.Close();
        }
    });
}
