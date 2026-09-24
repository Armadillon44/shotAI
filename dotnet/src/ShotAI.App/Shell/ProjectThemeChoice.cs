namespace ShotAI.App.Shell;

/// <summary>A View, Brand choice (spec 03 7.4.5): <paramref name="Brand"/> for the project at <paramref name="ProjectPath"/>, the one open at the click.</summary>
/// <param name="ProjectPath">The open project's folder when the row was clicked.</param>
/// <param name="Brand">The row's brand id, or null for App default, passed through untouched (INV-IPC-14).</param>
public sealed record ProjectThemeChoice(string ProjectPath, string? Brand);
