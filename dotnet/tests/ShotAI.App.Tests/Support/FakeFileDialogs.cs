using System.Windows;
using ShotAI.App.Services;

namespace ShotAI.App.Tests.Support;

/// <summary>
/// An <see cref="IFileDialogs"/> that shows nothing: each pick returns <see cref="Folder"/> or
/// <see cref="File"/> (null is a cancel), or throws <see cref="Throws"/>, and records its call.
/// </summary>
internal sealed class FakeFileDialogs : IFileDialogs
{
    /// <summary>What a folder pick returns; null is a cancel.</summary>
    public string? Folder { get; set; }

    /// <summary>What a file pick returns; null is a cancel.</summary>
    public string? File { get; set; }

    /// <summary>When set, a pick throws it.</summary>
    public Exception? Throws { get; set; }

    /// <summary>Each folder pick: its owner, title and starting folder.</summary>
    public List<(Window Owner, string Title, string? InitialDirectory)> FolderPicks { get; } = [];

    public string? PickFolder(Window owner, string title, string? initialDirectory)
    {
        FolderPicks.Add((owner, title, initialDirectory));
        if (Throws is { } e) throw e;
        return Folder;
    }

    public string? PickOpenFile(Window owner, string title, string filter)
    {
        if (Throws is { } e) throw e;
        return File;
    }
}
