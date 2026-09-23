namespace ShotAI.GenBrand;

/// <summary>
/// The contract cannot be generated. <see cref="Program"/> prints the message after
/// <c>gen-brand: </c> and exits 1, as <c>die()</c> does in <c>scripts/gen-brand.mjs</c>.
/// </summary>
public sealed class GenBrandException(string message) : Exception(message);
