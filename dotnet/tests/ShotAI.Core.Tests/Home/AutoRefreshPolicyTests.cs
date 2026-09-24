using ShotAI.Core.Home;
using Xunit;

namespace ShotAI.Core.Tests.Home;

/// <summary>Spec 06 8.4 (INV-HOME-15, D-HOME-2): the tick's interval and when it re-lists.</summary>
public sealed class AutoRefreshPolicyTests
{
    [Fact]
    public void TheIntervalIsTwentySeconds() => Assert.Equal(TimeSpan.FromMilliseconds(20_000), AutoRefreshPolicy.Interval);

    [Fact]
    public void TheRegroupIntervalIsAMinute() => Assert.Equal(TimeSpan.FromMinutes(1), AutoRefreshPolicy.RegroupInterval);

    public static TheoryData<bool, bool, bool, bool, bool> EveryCombination()
    {
        var data = new TheoryData<bool, bool, bool, bool, bool>();
        for (var bits = 0; bits < 32; bits++)
            data.Add((bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0, (bits & 8) != 0, (bits & 16) != 0);
        return data;
    }

    /// <summary>All 32 rows: only a showing Home with nothing under way ticks.</summary>
    [Theory]
    [MemberData(nameof(EveryCombination))]
    public void OnlyTheAllClearRowTicks(bool homeVisible, bool textInputFocused, bool renaming, bool selectionNonEmpty, bool anyBusy)
    {
        var allClear = homeVisible && !textInputFocused && !renaming && !selectionNonEmpty && !anyBusy;
        Assert.Equal(allClear, AutoRefreshPolicy.ShouldTick(homeVisible, textInputFocused, renaming, selectionNonEmpty, anyBusy));
    }

    [Fact]
    public void ExactlyOneRowTicks()
    {
        var ticking = Enumerable.Range(0, 32)
            .Where(bits => AutoRefreshPolicy.ShouldTick((bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0, (bits & 8) != 0, (bits & 16) != 0))
            .ToList();
        Assert.Equal([1], ticking);
    }
}
