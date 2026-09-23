using System.Text.Json.Nodes;
using ShotAI.Core.Json;

namespace ShotAI.Core.Tests.Conformance;

/// <summary>One shared fixture from contract/conformance/manifest/. Format: contract/conformance/README.md.</summary>
internal sealed record ConformanceCase(
    string FileName,
    string Name,
    string Why,
    string Status,
    string? Issue,
    string? Divergence,
    JsonObject Input,
    JsonObject Expect)
{
    public bool IsOpen => Status == "open";

    /// <summary>Where the linked contract/ copy lands in the test output.</summary>
    public static string ManifestCasesDir =>
        Path.Combine(AppContext.BaseDirectory, "contract", "conformance", "manifest");

    /// <summary>
    /// Every case file, sorted ordinally, as the TypeScript harness sorts them.
    /// </summary>
    public static IReadOnlyList<string> CaseFiles() =>
        Directory.Exists(ManifestCasesDir)
            ? Directory.GetFiles(ManifestCasesDir, "*.json")
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.Ordinal)
                .ToList()
            : [];

    /// <summary>
    /// Reads a case through <see cref="JsJson.Parse(ReadOnlySpan{byte})"/>, the parser the store
    /// uses, as the TypeScript harness reads it through <c>JSON.parse</c>: every number is a
    /// double before the codec sees it (AC-MODEL-36).
    /// </summary>
    public static ConformanceCase Load(string fileName)
    {
        var path = Path.Combine(ManifestCasesDir, fileName);
        var root = JsJson.Parse(File.ReadAllBytes(path)) as JsonObject
            ?? throw new InvalidDataException($"{fileName}: the case is not a JSON object");

        string? Str(string key) => root[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

        return new ConformanceCase(
            fileName,
            Str("name") ?? "",
            Str("why") ?? "",
            Str("status") ?? "",
            Str("issue"),
            Str("divergence"),
            root["input"] as JsonObject ?? throw new InvalidDataException($"{fileName}: input must be an object"),
            root["expect"] as JsonObject ?? throw new InvalidDataException($"{fileName}: expect must be an object"));
    }
}
