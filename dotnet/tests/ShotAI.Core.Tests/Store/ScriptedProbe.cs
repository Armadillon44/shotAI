using ShotAI.Core.Store;

namespace ShotAI.Core.Tests.Store;

/// <summary>An <see cref="IPathProbe"/> that answers from a script and records what it was asked.</summary>
internal sealed class ScriptedProbe(Func<string, PathKind> answer) : IPathProbe
{
    public List<string> Probed { get; } = [];

    public PathKind Probe(string fullPath)
    {
        Probed.Add(fullPath);
        return answer(fullPath);
    }
}
