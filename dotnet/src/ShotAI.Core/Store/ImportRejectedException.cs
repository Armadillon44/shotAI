using ShotAI.Core.Errors;

namespace ShotAI.Core.Store;

/// <summary>
/// An import would write where it must not (spec 01 2.9.9, 2.9.12, 7.13): a package entry
/// outside <c>shots/</c> and <c>export/.render/</c>, a path that leaves the project, or an
/// imported image whose <c>shots/</c> folder is a link (D-22).
/// </summary>
public sealed class ImportRejectedException : ShotAIException
{
    public ImportRejectedException(string message) : base(message) { }

    /// <summary><c>Package contains an unexpected file path: {rel}</c>, with the name as the package gave it.</summary>
    public static ImportRejectedException UnexpectedPath(string rel) => new($"Package contains an unexpected file path: {rel}");

    /// <summary><c>Refusing to extract a path outside the project: {rel}</c>.</summary>
    public static ImportRejectedException OutsideProject(string rel) => new($"Refusing to extract a path outside the project: {rel}");

    /// <summary>New native text for the <c>ImportStepAsync</c> refusal, which Electron has no string for (D-22).</summary>
    public static ImportRejectedException OutsideShots(string filename) => new($"Refusing to write outside the project: shots/{filename}");
}
