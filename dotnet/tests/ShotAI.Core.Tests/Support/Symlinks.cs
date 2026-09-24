using Xunit;

namespace ShotAI.Core.Tests.Support;

/// <summary>
/// Creates real symlinks. Windows needs Developer Mode or elevation for one, so a test that
/// cannot create it there skips; Linux always can. Junctions are Platform.Tests' business.
/// </summary>
internal static class Symlinks
{
    public static void Directory(string link, string target) =>
        Create(() => System.IO.Directory.CreateSymbolicLink(link, target));

    public static void File(string link, string target) =>
        Create(() => System.IO.File.CreateSymbolicLink(link, target));

    private static void Create(Action create)
    {
        try
        {
            create();
        }
        catch (Exception e) when (OperatingSystem.IsWindows() && e is UnauthorizedAccessException or IOException)
        {
            Assert.Skip($"this Windows session cannot create a symlink: {e.Message}");
        }
    }
}
