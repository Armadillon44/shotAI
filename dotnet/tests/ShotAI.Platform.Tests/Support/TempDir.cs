using ShotAI.Core.Store;
using ShotAI.Platform.FileSystem;

namespace ShotAI.Platform.Tests.Support;

/// <summary>A fresh folder under the temp directory, removed with everything in it on dispose.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir(string prefix = "shotai-") => Root = Directory.CreateTempSubdirectory(prefix).FullName;

    public string Root { get; }

    public string Combine(params string[] parts) => Path.Combine([Root, .. parts]);

    /// <summary>Writes a file, creating its folder.</summary>
    public string File(string relative, string text = "x")
    {
        var path = Combine(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, text);
        return path;
    }

    // Not Directory.Delete(Root, recursive: true): on a tree holding a junction it throws "The
    // parameter is incorrect", because it calls DeleteVolumeMountPoint on every mount-point tag
    // (spec 01 7.5). The reparse-safe walk is what the app uses anyway.
    public void Dispose() => ReparseSafeDelete.DeleteTree(Root, new WindowsPathProbe());
}
