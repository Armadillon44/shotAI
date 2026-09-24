using ShotAI.Core.Capture;
using ShotAI.Core.Model;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>
/// Spec 02 2.12 and 8.4: the climb that chooses the element a caption may name, and its
/// mapping to the step's element. A name reaches a step only from a chosen, actionable
/// element (INV-CAP-16).
/// </summary>
public sealed class ElementMappingTests
{
    private const int Button = 50000;
    private const int Edit = 50004;
    private const int MenuItem = 50011;
    private const int Text = 50020;
    private const int Pane = 50033;

    [Fact]
    public void AnActionableNamedElementIsAvailable() =>
        Assert.Equal(new StepElement(true, "OK", "Button", new Rect(10, 20, 100, 40)), ElementMapping.ToStep(true, "OK", Button, 10, 20, 110, 60));

    /// <summary>With nothing chosen the raw hit describes the step: its type and bounds, never its name, which may be page content.</summary>
    [Fact]
    public void ANonActionableHitKeepsItsTypeAndBoundsButNotItsName() =>
        Assert.Equal(new StepElement(false, null, "Text", new Rect(5, 6, 30, 10)), ElementMapping.ToStep(false, "Account number 12345", Text, 5, 6, 35, 16));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void AnEmptyNameIsUnavailable(string? name) =>
        Assert.Equal(new StepElement(false, null, "Button", new Rect(0, 0, 1, 1)), ElementMapping.ToStep(true, name, Button, 0, 0, 1, 1));

    [Fact]
    public void AnUnknownTypeIsNamedUnknown() =>
        Assert.Equal("Unknown", ElementMapping.ToStep(false, null, 0, 0, 0, 0, 0).ControlType);

    /// <summary>A bounding rectangle that could not be read is all zeros, and stays so.</summary>
    [Fact]
    public void UnreadBoundsAreZero() =>
        Assert.Equal(new Rect(0, 0, 0, 0), ElementMapping.ToStep(true, "OK", Button, 0, 0, 0, 0).Bounds);

    [Fact]
    public void AFailedQueryGivesTheUnavailableLiteral()
    {
        Assert.Same(StepElement.Unavailable, ElementMapping.Failed);
        Assert.Equal(new StepElement(false, null, null, null), ElementMapping.Failed);
    }

    [Fact]
    public void TheHitItselfWinsWhenItQualifies() =>
        Assert.Equal(0, ElementMapping.Choose([new("OK", Button), new("Dialog", Pane)]));

    /// <summary>A button's text block is the hit; the button, its parent, is the element the caption names.</summary>
    [Fact]
    public void TheNearestQualifyingAncestorWins() =>
        Assert.Equal(1, ElementMapping.Choose([new("OK", Text), new("OK", Button), new("Save", Button)]));

    [Fact]
    public void AnUnnamedActionableElementIsPassedOver() =>
        Assert.Equal(2, ElementMapping.Choose([new(null, Button), new("", MenuItem), new("Name", Edit)]));

    /// <summary>A whitespace name is not empty (EDGE-CAP-56), as in Rust's <c>is_empty</c>.</summary>
    [Fact]
    public void AWhitespaceNameQualifies() =>
        Assert.Equal(0, ElementMapping.Choose([new(" ", Button)]));

    /// <summary>At most six elements are examined: the hit and five ancestors.</summary>
    [Fact]
    public void TheClimbStopsAtSix()
    {
        var five = Enumerable.Repeat(new ElementFacts("t", Text), 5).ToList();
        Assert.Equal(5, ElementMapping.Choose([.. five, new("OK", Button)]));
        Assert.Equal(-1, ElementMapping.Choose([.. five, new("t", Text), new("OK", Button)]));
    }

    [Fact]
    public void NothingQualifyingIsMinusOne()
    {
        Assert.Equal(-1, ElementMapping.Choose([]));
        Assert.Equal(-1, ElementMapping.Choose([new("Some text", Text), new("Page", Pane)]));
    }

    /// <summary>Each element costs a cross-process call, so the climb reads no further than it goes, not even the sixth element's parent.</summary>
    [Fact]
    public void TheClimbReadsLazily()
    {
        var read = 0;
        IEnumerable<ElementFacts> Climb()
        {
            for (var i = 0; ; i++)
            {
                read++;
                yield return i == 2 ? new("OK", Button) : new("t", Text);
            }
        }
        Assert.Equal(2, ElementMapping.Choose(Climb()));
        Assert.Equal(3, read);

        read = 0;
        IEnumerable<ElementFacts> Endless()
        {
            while (true)
            {
                read++;
                yield return new("t", Text);
            }
        }
        Assert.Equal(-1, ElementMapping.Choose(Endless()));
        Assert.Equal(6, read);
    }

    [Fact]
    public void ArgumentsAreChecked() => Assert.Throws<ArgumentNullException>(() => ElementMapping.Choose(null!));
}
