using ShotAI.Core.Model;

namespace ShotAI.Core.Store;

/// <summary>
/// An opened project: its resolved folder and the manifest read from it (spec 01 7.8). It
/// replaces Electron's opaque <c>projectId</c>, which existed only for the <c>shot://</c>
/// protocol (EDGE-IPC-26).
/// </summary>
public sealed record OpenedProject(string Dir, ProjectManifest Manifest);
