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

    // A recursive delete removes a junction without following it.
    public void Dispose() => Directory.Delete(Root, recursive: true);
}
