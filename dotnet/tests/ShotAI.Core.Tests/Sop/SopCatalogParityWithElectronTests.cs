using System.Globalization;
using System.Text.RegularExpressions;
using ShotAI.Core.Sop;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Sop;

/// <summary>
/// Every catalog string and number equals the Electron source it was copied from (spec 07 7.2:
/// the strings must match exactly): <c>src/shared/sop.ts</c>, <c>src/main/claude-models.ts</c>
/// and <c>src/main/claude-service.ts</c>. The sources are read with strict patterns, so a
/// format change there fails loudly instead of comparing nothing.
/// </summary>
public sealed partial class SopCatalogParityWithElectronTests
{
    [Fact]
    public void OptionListsEqualTheTypeScriptSource()
    {
        var sop = ElectronSource.Read("src/shared/sop.ts");

        Assert.Equal(
            Entries(Block(sop, "SOP_MODELS"), ModelEntry()),
            SopCatalog.Models.Select(m => (m.Id, m.Label, m.Blurb)));
        Assert.Equal(
            Entries(Block(sop, "SOP_EFFORTS"), OneLineEntry()),
            SopCatalog.Efforts.Select(e => (e.Wire, e.Label, e.Blurb)));
        Assert.Equal(
            Entries(Block(sop, "SOP_TONES"), OneLineEntry()),
            SopCatalog.Tones.Select(t => (t.Wire, t.Label, t.Blurb)));
        Assert.Equal(SopCatalog.CustomInstructionsMax, Number(sop, @"export const SOP_CUSTOM_INSTRUCTIONS_MAX = (\d+);"));
        Assert.Equal(SopModelIds.Sonnet5, Match(sop, @"export const DEFAULT_SOP_MODEL: SopModelId = '([^']+)';"));
        Assert.Equal(SopCatalog.ToWire(SopSettings.Default.Effort), Match(sop, @"export const DEFAULT_SOP_EFFORT: SopEffort = '([^']+)';"));
        Assert.Equal(SopCatalog.ToWire(SopSettings.Default.Tone), Match(sop, @"export const DEFAULT_SOP_TONE: SopTone = '([^']+)';"));
    }

    [Fact]
    public void TonePromptsEqualTheTypeScriptSource()
    {
        var models = ElectronSource.Read("src/main/claude-models.ts");
        var prompts = Regex.Matches(Block(models, "TONE_PROMPT", open: '{', close: '}'), @"(\w+):\s*'([^']*)',")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.Ordinal);

        Assert.Equal(SopCatalog.Tones.Count, prompts.Count);
        foreach (var (id, wire, _, _) in SopCatalog.Tones)
            Assert.Equal(prompts[wire], SopCatalog.TonePrompt[id]);
    }

    [Fact]
    public void ModelParamsEqualTheTypeScriptSource()
    {
        var models = ElectronSource.Read("src/main/claude-models.ts");
        var block = Block(models, "MODEL_PARAMS", open: '{', close: '}');
        var p = SopCatalog.Params[SopModelIds.Sonnet5];

        Assert.Single(SopCatalog.Params);
        Assert.Contains("'" + SopModelIds.Sonnet5 + "': {", block, StringComparison.Ordinal);
        Assert.Equal(p.AdaptiveThinking ? "{ type: 'adaptive' }" : "null", Match(block, @"thinking: (\{ type: 'adaptive' \}|null),"));
        Assert.Equal(p.SupportsEffort ? "true" : "false", Match(block, @"supportsEffort: (true|false),"));
        Assert.Equal(p.InputPerMTok, Number(block, @"inputPerMTok: ([\d.]+),"));
        Assert.Equal(p.OutputPerMTok, Number(block, @"outputPerMTok: ([\d.]+),"));
        Assert.Equal(p.MaxTokens, Number(block, @"maxTokens: ([\d.]+),"));
        Assert.Equal(SopCatalog.EstOutputTokens, Number(ElectronSource.Read("src/main/claude-service.ts"), @"const EST_OUTPUT_TOKENS = (\d+);"));
    }

    // The text between `export const <name>...= [` (or `{`) and the matching `];` (or `};`).
    private static string Block(string source, string name, char open = '[', char close = ']')
    {
        var start = source.IndexOf("export const " + name, StringComparison.Ordinal);
        Assert.True(start >= 0, $"no {name} in the source");
        var body = source.IndexOf(" = " + open, start, StringComparison.Ordinal);
        var end = source.IndexOf("\n" + close + ";", body, StringComparison.Ordinal);
        Assert.True(body >= 0 && end > body, $"{name} is not a {open}...{close} literal");
        return source[(body + 3)..end];
    }

    private static List<(string, string, string)> Entries(string block, Regex entry)
    {
        var found = entry.Matches(block).Select(m => (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value)).ToList();
        Assert.Equal(Regex.Matches(block, @"\bid: '").Count, found.Count);
        return found;
    }

    private static string Match(string text, string pattern)
    {
        var m = Regex.Match(text, pattern);
        Assert.True(m.Success, $"no match for {pattern}");
        return m.Groups[1].Value;
    }

    private static double Number(string text, string pattern) => double.Parse(Match(text, pattern), CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\{\s*id: '([^']*)',\s*label: '([^']*)',\s*blurb: '([^']*)',\s*\}")]
    private static partial Regex ModelEntry();

    [GeneratedRegex(@"\{ id: '([^']*)', label: '([^']*)', blurb: '([^']*)' \}")]
    private static partial Regex OneLineEntry();
}
