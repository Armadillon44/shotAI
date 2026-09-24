using System.Runtime.InteropServices;
using ShotAI.Platform.Tests.Support;
using Xunit;

namespace ShotAI.Platform.Tests.Startup;

/// <summary>
/// INV-PKG-16 (spec 12 7.9.1 and 8.2): once <see cref="DllSearchHardening.Apply"/> has run, a
/// DLL named without a path is not looked for in the current folder.
/// </summary>
[Collection(DllSearchCollection.Name)]
public sealed partial class DllSearchTests
{
    private const int ErrorModNotFound = 126;
    private const string Probe = "shotai_test_probe.dll";

    /// <summary>
    /// A file named like a DLL, not a valid image, in the current folder: before the hardening
    /// the default search finds it there and fails to load it; after it, the file is not found
    /// at all, although its full path still reaches it. A second call is harmless.
    /// </summary>
    [Fact]
    public void CurrentDirectoryNotSearched()
    {
        using var temp = new TempDir();
        File.WriteAllBytes(Path.Combine(temp.Root, Probe), [0x4d, 0x5a, 0, 0]);
        var saved = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = temp.Root;
            Assert.NotEqual(ErrorModNotFound, LoadError(Probe));
            Assert.True(DllSearchHardening.Apply());
            Assert.Equal(ErrorModNotFound, LoadError(Probe));
            Assert.NotEqual(ErrorModNotFound, LoadError(Path.Combine(temp.Root, Probe)));
            Assert.True(DllSearchHardening.Apply());
        }
        finally
        {
            Environment.CurrentDirectory = saved;
        }
    }

    // The Win32 error of LoadLibraryExW with no flags; a load that succeeds would be a bug here.
    private static int LoadError(string name)
    {
        var module = LoadLibraryExW(name, 0, 0);
        if (module != 0)
        {
            FreeLibrary(module);
            Assert.Fail($"{name} loaded");
        }
        return Marshal.GetLastPInvokeError();
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint LoadLibraryExW(string lpLibFileName, nint hFile, uint dwFlags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeLibrary(nint hLibModule);
}

/// <summary>The current folder and the DLL search are process-wide, so these tests run alone.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DllSearchCollection
{
    public const string Name = "dll search";
}
