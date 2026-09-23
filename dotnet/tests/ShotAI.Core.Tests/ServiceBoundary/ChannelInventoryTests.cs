using System.Text.RegularExpressions;
using Xunit;

namespace ShotAI.Core.Tests.ServiceBoundary;

/// <summary>
/// The checked-in map of every Electron IPC channel to the native member that replaces it
/// (spec 11 7.4 and 8.2, INV-IPC-4, AC-IPC-1).
/// </summary>
public sealed partial class ChannelInventoryTests
{
    private static readonly string[] Classes = ["REQUIRED", "IMPROVEMENT", "ELECTRON-ONLY"];
    private static readonly string[] Modes = ["Read", "Optimistic", "Durable", "Command", "Event", "Progress", "Local"];

    [Fact]
    public void MapHas87UniqueChannels()
    {
        var rows = ChannelMap.Load();
        Assert.Equal(87, rows.Count);
        Assert.Equal(87, rows.Select(r => r.Channel).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(87, rows.Select(r => r.Row).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void SplitIs74Invoke3Send10Push()
    {
        var rows = ChannelMap.Load();
        Assert.Equal(74, rows.Count(r => r.Kind == "invoke"));
        Assert.Equal(3, rows.Count(r => r.Kind == "send"));
        Assert.Equal(10, rows.Count(r => r.Kind == "push"));
    }

    [Fact]
    public void EveryRowHasOneTargetAndAClass()
    {
        foreach (var r in ChannelMap.Load())
        {
            Assert.Matches(ChannelName(), r.Channel);
            Assert.Matches(RowId(), r.Row);
            Assert.True(MemberName().IsMatch(r.Member), $"{r.Row}: member '{r.Member}' is not <namespace>.<Type>.<Member>");
            Assert.Contains(r.Mode, Modes);
            Assert.Contains(r.Class, Classes);
            Assert.Matches(OwnerSpec(), r.Owner);
            // Push rows are E1 to E10; the three sends are C10, K11 and K12 (spec 11 7.4.2, 7.4.3).
            var expectedKind = r.Row[0] == 'E' ? "push" : r.Row is "C10" or "K11" or "K12" ? "send" : "invoke";
            Assert.True(expectedKind == r.Kind, $"{r.Row}: kind '{r.Kind}', expected '{expectedKind}'");
        }
    }

    [Fact]
    public void MatchesElectronWhileItExists()
    {
        var ipc = Path.Combine(RepoRoot(), "src", "shared", "ipc.ts");
        if (!File.Exists(ipc))
            Assert.Skip("src/shared/ipc.ts is gone (after cutover); the channel map is now the record.");

        var electron = IpcChannelValue().Matches(File.ReadAllText(ipc))
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        var native = ChannelMap.Load().Select(r => r.Channel).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(electron.Except(native));
        Assert.Empty(native.Except(electron));
    }

    /// <summary>The first ancestor of the test output that holds dotnet/ShotAI.slnx.</summary>
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "dotnet", "ShotAI.slnx"))) return dir.FullName;
        }
        throw new InvalidOperationException($"no ancestor of {AppContext.BaseDirectory} contains dotnet/ShotAI.slnx");
    }

    // The regex spec 11 8.2 gives for the IpcChannels values of src/shared/ipc.ts.
    [GeneratedRegex(@"^\s+\w+:\s*'([a-z-]+:[a-z-]+)',", RegexOptions.Multiline)]
    private static partial Regex IpcChannelValue();

    [GeneratedRegex(@"^[a-z-]+:[a-z-]+$")]
    private static partial Regex ChannelName();

    [GeneratedRegex(@"^[IPSXGUCKE][0-9]{1,2}$")]
    private static partial Regex RowId();

    [GeneratedRegex(@"^ShotAI\.(Core|Platform|App)(\.[A-Za-z][A-Za-z0-9]*)+(\([a-z][A-Za-z0-9]*\))?$")]
    private static partial Regex MemberName();

    [GeneratedRegex(@"^(0[1-9]|1[0-2])$")]
    private static partial Regex OwnerSpec();
}
