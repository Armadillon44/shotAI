namespace ShotAI.Core.Store;

/// <summary>
/// One file of an imported package (spec 01 2.9.9): its package path and its bytes, already
/// whitelisted and magic-byte checked by the package reader (spec 09 7.12).
/// </summary>
/// <param name="Rel">The entry name; <c>\</c> is read as <c>/</c>.</param>
public sealed record ImportFile(string Rel, ReadOnlyMemory<byte> Bytes);
