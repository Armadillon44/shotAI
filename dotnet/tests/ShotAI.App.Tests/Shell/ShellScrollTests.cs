using System.Windows.Controls;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using Xunit;
using static ShotAI.App.Tests.Support.ListingProjects;

namespace ShotAI.App.Tests.Shell;

/// <summary>
/// Spec 06 8.4, INV-HOME-19, 2.19 and 7.7: Home keeps its offset across the other views; the
/// project view starts every open at the top (05 EDGE-REP-42). The Settings view, which also
/// starts at the top, joins with WP-B10.
/// </summary>
public sealed class ShellScrollTests
{
    /// <summary>Home's offset is restored after a project and after Settings.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task HomeOffsetRestored(bool viaProject) => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Projects.Listing = [.. Enumerable.Range(0, 40).Select(i => Project($@"C:\p\{i:D2}", $"Project {i:D2}", "2026-07-22T09:00:00.000Z"))];
        var view = new ShellView { DataContext = t.Shell };
        var window = TestShell.Host(view);
        window.Show();
        try
        {
            t.Shell.Start();
            await TestShell.Settle();
            var scroller = view.HomeView.ScrollViewer;
            scroller.ScrollToVerticalOffset(350);
            await TestShell.Settle();
            Assert.Equal(350, scroller.VerticalOffset, 3);

            if (viaProject) t.Shell.ShowProject(@"C:\p\05", null);
            else t.Shell.OpenSettings();
            await TestShell.Settle();
            Assert.False(view.HomeView.IsVisible);

            if (viaProject) t.Shell.CloseProject();
            else t.Shell.CloseSettings();
            await TestShell.Settle();
            Assert.True(view.HomeView.IsVisible);
            Assert.Equal(350, scroller.VerticalOffset, 3);
            Assert.Equal(350, view.HomeView.ScrollMemory.Saved, 3);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>05 EDGE-REP-42: a project opens at the top, wherever the report was left before Back.</summary>
    [Fact]
    public Task TheProjectStartsAtTheTop() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        ShotAI.Core.Model.ProjectStep[] many = [.. Enumerable.Range(1, 30).Select(i => Manifests.Text($"t{i}", heading: $"Step {i}", body: "Some text"))];
        t.Projects.CanOpen(@"C:\p\a", Manifests.Of("A", many));
        t.Projects.CanOpen(@"C:\p\b", Manifests.Of("B", many));
        var view = new ShellView { DataContext = t.Shell };
        var window = TestShell.Host(view, height: 500);
        window.Show();
        try
        {
            t.Shell.Start();
            await t.Shell.OpenProjectAsync(@"C:\p\a");
            await TestShell.Settle();
            var scroller = view.ProjectView.ScrollViewer;
            Assert.True(scroller.ScrollableHeight > 600);
            scroller.ScrollToVerticalOffset(600);
            await TestShell.Settle();
            Assert.Equal(600, scroller.VerticalOffset, 3);

            t.Project.BackCommand.Execute(null);
            await TestShell.Settle();
            await t.Shell.OpenProjectAsync(@"C:\p\b");
            await TestShell.Settle();
            Assert.Equal(ShellViewKind.Project, t.Shell.CurrentView);
            Assert.Equal(0, scroller.VerticalOffset, 3);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// 2.19: the offset is recorded as the view scrolls, never after it is left, when shorter
    /// content may already have clamped it; the return restores it, then records again.
    /// </summary>
    [Fact]
    public Task WhatScrollsAfterTheLeaveIsNotRecorded() => Sta.RunAsync(async () =>
    {
        var scroller = new ScrollViewer { Content = new Border { Height = 3000 } };
        var memory = new ScrollMemory(scroller);
        var window = TestShell.Host(scroller);
        window.Show();
        try
        {
            memory.Enter();
            await TestShell.Settle();
            scroller.ScrollToVerticalOffset(400);
            await TestShell.Settle();
            Assert.Equal(400, memory.Saved, 3);

            memory.Leave();
            scroller.ScrollToVerticalOffset(0);
            await TestShell.Settle();
            Assert.Equal(0, scroller.VerticalOffset, 3);
            Assert.Equal(400, memory.Saved, 3);

            memory.Enter();
            await TestShell.Settle();
            Assert.Equal(400, scroller.VerticalOffset, 3);
            scroller.ScrollToVerticalOffset(120);
            await TestShell.Settle();
            Assert.Equal(120, memory.Saved, 3);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>The restore is clamped to the content as it is on the return (2.19, WPF clamps).</summary>
    [Fact]
    public Task TheRestoreIsClamped() => Sta.RunAsync(async () =>
    {
        var content = new Border { Height = 3000 };
        var scroller = new ScrollViewer { Content = content };
        var memory = new ScrollMemory(scroller);
        var window = TestShell.Host(scroller, height: 500);
        window.Show();
        try
        {
            memory.Enter();
            await TestShell.Settle();
            scroller.ScrollToVerticalOffset(2000);
            await TestShell.Settle();
            memory.Leave();
            content.Height = 800;
            memory.Enter();
            await TestShell.Settle();
            Assert.Equal(scroller.ScrollableHeight, scroller.VerticalOffset, 3);
            Assert.True(scroller.VerticalOffset < 2000);
        }
        finally
        {
            window.Close();
        }
    });
}
