using ShotAI.Core.Shell;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Shell;

/// <summary>
/// Spec 03 8.3 (INV-SHELL-22): each string equals its 2.11 text. The strings arrive with the
/// windows that show them; the em-dash cases join with the pill's and the overlay's titles.
/// </summary>
public sealed class ShellStringsTests
{
    [Fact]
    public void AppNameIsTheMainWindowTitle() => Assert.Equal("shotAI", ShellStrings.AppName);

    /// <summary>The same text as the Electron window's <c>title</c> (<c>src/main/main.ts:270</c>).</summary>
    [Fact]
    public void AppNameMatchesTheElectronTitle() =>
        Assert.Contains("    title: '" + ShellStrings.AppName + "',\n", ElectronSource.Read("src/main/main.ts").ReplaceLineEndings("\n"), StringComparison.Ordinal);
}
