using ShotAI.Core.Home;
using Xunit;

namespace ShotAI.Core.Tests.Home;

/// <summary>
/// Spec 06 8.4 and 2.13: the inline rename commits at most once, a cancel never commits, and a
/// title that is empty or unchanged once trimmed writes nothing (AC-HOME-11, EDGE-HOME-10).
/// </summary>
public sealed class RenameSessionTests
{
    private static readonly Dictionary<string, string> Titles = new(StringComparer.Ordinal)
    {
        ["a"] = "Onboarding",
        ["b"] = "Payroll run",
    };

    private static string? TitleOf(string path) => Titles.GetValueOrDefault(path);

    private static RenameSession Open(string path, string typed)
    {
        var session = new RenameSession();
        Assert.Null(session.Begin(path, Titles[path], TitleOf));
        Assert.Equal((path, Titles[path], true), (session.Path, session.Value, session.IsOpen));
        session.Value = typed;
        return session;
    }

    [Fact]
    public void CommitTrimmed()
    {
        var session = Open("a", "  New hire setup \t\n");
        Assert.Equal(new RenameCommit("a", "New hire setup"), session.Commit(TitleOf));
        Assert.False(session.IsOpen);
        Assert.Null(session.Path);
    }

    /// <summary>AC-HOME-11: the same name with spaces around it, or nothing but spaces, writes nothing.</summary>
    [Theory]
    [InlineData("Onboarding")]
    [InlineData("  Onboarding  ")]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrUnchangedWritesNothing(string typed)
    {
        var session = Open("a", typed);
        Assert.Null(session.Commit(TitleOf));
        Assert.False(session.IsOpen);
    }

    /// <summary>Unchanged is ordinal, as JavaScript's ===: a change of case is a new title.</summary>
    [Fact]
    public void ACaseChangeIsANewTitle() => Assert.Equal(new RenameCommit("a", "onboarding"), Open("a", "onboarding").Commit(TitleOf));

    /// <summary>Enter commits and ends the session, so the focus loss that follows finds nothing to commit.</summary>
    [Fact]
    public void CommitAtMostOnce()
    {
        var session = Open("a", "Renamed");
        Assert.NotNull(session.Commit(TitleOf));
        Assert.Null(session.Commit(TitleOf));
        Assert.Null(session.Commit(TitleOf));
    }

    /// <summary>Escape ends the session without a write, and the focus loss after it commits nothing.</summary>
    [Fact]
    public void EscapeNeverCommits()
    {
        var session = Open("a", "Renamed");
        session.Cancel();
        Assert.False(session.IsOpen);
        Assert.Null(session.Commit(TitleOf));
    }

    /// <summary>Rename on another row: the open rename ends as focus loss ends it, by a commit handed back, and the new one opens.</summary>
    [Fact]
    public void BeginOnAnotherRowCommitsFirst()
    {
        var session = Open("a", "Renamed");
        Assert.Equal(new RenameCommit("a", "Renamed"), session.Begin("b", Titles["b"], TitleOf));
        Assert.Equal(("b", "Payroll run"), (session.Path, session.Value));
        Assert.Equal(new RenameCommit("b", "Payroll run Q3"), CommitAs(session, "Payroll run Q3"));

        // An open rename with nothing to write ends as quietly.
        var unchanged = Open("a", "Onboarding");
        Assert.Null(unchanged.Begin("b", Titles["b"], TitleOf));
        Assert.Equal("b", unchanged.Path);
    }

    /// <summary>The row vanished in a refresh: the rename ends with no write and no later commit (EDGE-HOME-11).</summary>
    [Fact]
    public void AbandonWritesNothing()
    {
        var session = Open("a", "Renamed");
        session.Abandon();
        Assert.False(session.IsOpen);
        Assert.Null(session.Commit(TitleOf));
        Assert.Null(session.Begin("b", Titles["b"], TitleOf));
    }

    /// <summary>The trim is JavaScript's: U+FEFF and the Unicode spaces go, U+0085 stays.</summary>
    [Fact]
    public void TrimUsesJsSemantics()
    {
        Assert.Equal(new RenameCommit("a", "Renamed"), Open("a", "\ufeff\u00a0Renamed\u3000\u2028").Commit(TitleOf));
        Assert.Equal(new RenameCommit("a", "\u0085Renamed"), Open("a", "\u0085Renamed").Commit(TitleOf));
        Assert.Null(Open("a", "\ufeff Onboarding \u2029").Commit(TitleOf));
    }

    /// <summary>The title compared is the list's at the commit, not the one the box opened with.</summary>
    [Fact]
    public void ComparedWithTheTitleAtTheCommit()
    {
        var session = Open("a", "Onboarding");
        Assert.Equal(new RenameCommit("a", "Onboarding"), session.Commit(_ => "Onboarding v2"));
        // A path the list no longer shows is compared with nothing, as Electron's find() gave undefined; Home abandons first.
        Assert.Equal(new RenameCommit("a", "Onboarding"), Open("a", "Onboarding").Commit(_ => null));
    }

    [Fact]
    public void ANewSessionIsClosed()
    {
        var session = new RenameSession();
        Assert.Equal(((string?)null, "", false), (session.Path, session.Value, session.IsOpen));
        Assert.Null(session.Commit(TitleOf));
        session.Cancel();
        session.Abandon();
        Assert.False(session.IsOpen);
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        var session = new RenameSession();
        Assert.Throws<ArgumentNullException>(() => session.Begin(null!, "t", TitleOf));
        Assert.Throws<ArgumentNullException>(() => session.Begin("a", null!, TitleOf));
        Assert.Throws<ArgumentNullException>(() => session.Begin("a", "t", null!));
        Assert.Throws<ArgumentNullException>(() => session.Commit(null!));
        Assert.False(session.IsOpen);
    }

    private static RenameCommit? CommitAs(RenameSession session, string typed)
    {
        session.Value = typed;
        return session.Commit(TitleOf);
    }
}
