using System.Text.Json;

namespace ShotAI.Core.Tests.ServiceBoundary;

/// <summary>One row of <c>channel-map.json</c> (spec 11 8.2).</summary>
internal sealed record ChannelRow(
    string Channel, string Kind, string Row, string Member, string Mode, string Class, string Owner);

/// <summary>Reads <c>ServiceBoundary/channel-map.json</c> from the test output.</summary>
internal static class ChannelMap
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "ServiceBoundary", "channel-map.json");

    public static IReadOnlyList<ChannelRow> Load() =>
        JsonSerializer.Deserialize<List<ChannelRow>>(File.ReadAllText(FilePath), Options)
        ?? throw new InvalidOperationException($"{FilePath} is empty");
}
