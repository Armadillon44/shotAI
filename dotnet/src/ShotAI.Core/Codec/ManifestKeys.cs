using System.Collections.Frozen;

namespace ShotAI.Core.Codec;

/// <summary>
/// <c>MANIFEST_KEYS</c> (<c>src/shared/project.ts:511-527</c>): every root key this build names.
/// A root key not in this set is an extra and is kept verbatim (INV-MODEL-1).
/// </summary>
/// <remarks>
/// An exact list of names, never derived from what <see cref="ManifestCodec.Encode"/> writes.
/// <c>displayScale</c>, <c>theme</c> and <c>introEditedByUser</c> are written only
/// conditionally; a set derived from the output would call a deliberately omitted key
/// unknown, and the extras copy would put back the value the coercion rejected
/// (INV-MODEL-2, Electron <c>77adda3</c>).
/// </remarks>
public static class ManifestKeys
{
    /// <summary>The 15 names, compared ordinally.</summary>
    public static readonly FrozenSet<string> All = new[]
    {
        "version",
        "id",
        "title",
        "createdWith",
        "createdAt",
        "updatedAt",
        "captureSettings",
        "steps",
        "displayScale",
        "theme",
        "intro",
        "introEditedByUser",
        "sopBackup",
        "archived",
        "archivedAt",
    }.ToFrozenSet(StringComparer.Ordinal);
}
