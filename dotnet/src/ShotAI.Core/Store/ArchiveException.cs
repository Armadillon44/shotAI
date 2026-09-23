using ShotAI.Core.Errors;

namespace ShotAI.Core.Store;

/// <summary>
/// Archiving or restoring refused or failed closed (spec 01 2.9.13, 7.13), with Electron's texts:
/// nothing the user can lose was deleted.
/// </summary>
public sealed class ArchiveException : ShotAIException
{
    public ArchiveException(string message) : base(message) { }

    public ArchiveException(string message, Exception? inner) : base(message, inner) { }

    /// <summary>The written zip does not hold exactly the files collected, byte for byte (D-12).</summary>
    public static ArchiveException VerificationFailed(int got, int expected, Exception? inner = null) =>
        new($"archive verification failed ({got} entries, expected {expected}) \u2014 nothing deleted", inner);

    /// <summary>An entry outside <c>shots/</c> and <c>export/</c>, or a name with a <c>.</c>, <c>..</c> or empty segment (D-13, D-25).</summary>
    public static ArchiveException UnexpectedPath(string name) => new($"archive contains an unexpected path: {name}");

    /// <summary>An entry that confinement refuses, a link on its way included (lowercase <c>r</c>, as Electron writes it).</summary>
    public static ArchiveException OutsideProject(string name) => new($"refusing to extract a path outside the project: {name}");
}
