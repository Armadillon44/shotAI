namespace ShotAI.Core.Store;

/// <summary>The payload of <see cref="IProjectSession.Changed"/> (spec 01 7.10).</summary>
/// <param name="Kind">Why the manifest changed.</param>
/// <param name="AffectedStepIds">
/// The steps a <see cref="ManifestChangeKind.Local"/> change touched, from
/// <see cref="ProjectOperation.AffectedStepIds"/>; null when the change is structural or not
/// local (R-ARCH-20).
/// </param>
public sealed record ManifestChangedEventArgs(ManifestChangeKind Kind, IReadOnlyList<string>? AffectedStepIds);
