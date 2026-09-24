namespace ShotAI.Core.Home;

/// <summary>The title a rename writes (spec 06 7.3).</summary>
/// <param name="Path">The project renamed.</param>
/// <param name="Title">Its new title, trimmed.</param>
public sealed record RenameCommit(string Path, string Title);
