namespace ShotAI.Core.Tests.Support;

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

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A test left a read-only file behind; clear the attribute and try once more.
            foreach (var f in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                System.IO.File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
    }
}
