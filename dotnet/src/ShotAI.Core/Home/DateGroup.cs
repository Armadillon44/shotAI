namespace ShotAI.Core.Home;

/// <summary>One non-empty date span of <see cref="DateGroups.Group"/>, its items in their input order.</summary>
public sealed record DateGroup<T>(DateBucket Bucket, IReadOnlyList<T> Items);
