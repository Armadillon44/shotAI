using System.Diagnostics;
using Xunit;

namespace ShotAI.Platform.Tests.Support;

/// <summary>
/// Junctions and symlinks for the reparse-point tests. A junction needs no privilege, so its
/// creation must work and a failure fails the test (AC-MODEL-11). A symlink needs Developer
/// Mode or elevation, so a test that cannot create one skips.
/// </summary>
internal static class Links
{
    /// <summary><c>mklink /J</c>: .NET has no API that creates a junction.</summary>
    public static void Junction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"mklink /J failed ({process.ExitCode}): {output}");
        Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0, "mklink /J made no reparse point");
    }

    public static void SymlinkDirectory(string link, string target) =>
        Symlink(() => Directory.CreateSymbolicLink(link, target));

    public static void SymlinkFile(string link, string target) =>
        Symlink(() => File.CreateSymbolicLink(link, target));

    private static void Symlink(Action create)
    {
        try
        {
            create();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            Assert.Skip($"this session cannot create a symlink (Developer Mode or elevation needed): {e.Message}");
        }
    }
}
