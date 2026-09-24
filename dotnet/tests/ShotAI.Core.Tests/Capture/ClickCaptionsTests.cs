using ShotAI.Core.Capture;
using ShotAI.Core.Model;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>
/// Spec 02 2.9 and 8.3: the caption phrasing, ported from <c>src/main/click-caption.test.ts</c>,
/// with every row of 2.9.1 and the hotkey captions.
/// </summary>
public sealed class ClickCaptionsTests
{
    private static StepElement El(string? name, string? controlType) => new(true, name, controlType, null);

    [Theory]
    [InlineData("Button", "button")]
    [InlineData("SplitButton", "button")]
    [InlineData("Hyperlink", "link")]
    [InlineData("CheckBox", "checkbox")]
    [InlineData("RadioButton", "option")]
    [InlineData("Tab", "tab")]
    [InlineData("TabItem", "tab")]
    [InlineData("Edit", "field")]
    [InlineData("ComboBox", "dropdown")]
    [InlineData("ListItem", "item")]
    [InlineData("TreeItem", "item")]
    [InlineData("DataItem", "item")]
    [InlineData("Slider", "slider")]
    [InlineData("Spinner", "spinner")]
    public void KnownTypesHaveANoun(string controlType, string word) => Assert.Equal(word, ClickCaptions.ControlWord(controlType));

    /// <summary>A menu item, any other type, null, and a type in the wrong case leave the noun out.</summary>
    [Theory]
    [InlineData("MenuItem")]
    [InlineData("Whatever")]
    [InlineData("Custom")]
    [InlineData("")]
    [InlineData("button")]
    [InlineData(null)]
    public void OtherTypesHaveNone(string? controlType) => Assert.Equal("", ClickCaptions.ControlWord(controlType));

    [Fact]
    public void ANamedControlGetsItsNoun()
    {
        Assert.Equal("Click 'OK' button in Notepad", ClickCaptions.Build(MouseButton.Left, false, "Notepad", El("OK", "Button")));
        Assert.Equal("Click 'Docs' link in Chrome", ClickCaptions.Build(MouseButton.Left, false, "Chrome", El("Docs", "Hyperlink")));
    }

    [Fact]
    public void AMenuItemIsASelection() =>
        Assert.Equal("Select 'Copy' in Notepad", ClickCaptions.Build(MouseButton.Left, true, "Notepad", El("Copy", "MenuItem")));

    /// <summary>A named menu item is a selection whatever the menu tracking said, and whichever button.</summary>
    [Fact]
    public void AMenuItemIsASelectionWithoutTheMenuFlagOrWithTheRightButton()
    {
        Assert.Equal("Select 'Copy' in Notepad", ClickCaptions.Build(MouseButton.Left, false, "Notepad", El("Copy", "MenuItem")));
        Assert.Equal("Select 'Copy' in Notepad", ClickCaptions.Build(MouseButton.Right, false, "Notepad", El("Copy", "MenuItem")));
    }

    [Fact]
    public void ARightClickOnANamedControl() =>
        Assert.Equal("Right-click 'File' button in Notepad", ClickCaptions.Build(MouseButton.Right, false, "Notepad", El("File", "Button")));

    [Fact]
    public void AnUnknownTypeLeavesTheNounOut()
    {
        Assert.Equal("Click 'Thing' in Notepad", ClickCaptions.Build(MouseButton.Left, false, "Notepad", El("Thing", null)));
        Assert.Equal("Right-click 'Thing' in Notepad", ClickCaptions.Build(MouseButton.Right, false, "Notepad", El("Thing", "Custom")));
    }

    /// <summary>With the proximity gate's flag on a named dialog button, the button wins over "Select from context menu".</summary>
    [Fact]
    public void ANamedControlWinsOverTheMenuFlag() =>
        Assert.Equal("Click 'OK' button in Properties", ClickCaptions.Build(MouseButton.Left, true, "Properties", El("OK", "Button")));

    [Fact]
    public void TheMiddleAndOtherButtonsClick()
    {
        Assert.Equal("Click 'Tab 2' tab in Chrome", ClickCaptions.Build(MouseButton.Middle, false, "Chrome", El("Tab 2", "TabItem")));
        Assert.Equal("Click in Chrome", ClickCaptions.Build(MouseButton.Other, false, "Chrome", null));
    }

    [Fact]
    public void WithNoNameTheWindowAlone()
    {
        Assert.Equal("Click in Notepad", ClickCaptions.Build(MouseButton.Left, false, "Notepad", null));
        Assert.Equal("Right-click in Notepad", ClickCaptions.Build(MouseButton.Right, false, "Notepad", null));
        Assert.Equal("Select from context menu in Notepad", ClickCaptions.Build(MouseButton.Left, true, "Notepad", null));
        Assert.Equal("Click in Notepad", ClickCaptions.Build(MouseButton.Left, false, "Notepad", El(null, "Button")));
    }

    /// <summary>An empty name is JavaScript-falsy; the right button comes before the menu flag.</summary>
    [Fact]
    public void AnEmptyNameCountsAsNone()
    {
        Assert.Equal("Click in Notepad", ClickCaptions.Build(MouseButton.Left, false, "Notepad", El("", "Button")));
        Assert.Equal("Right-click in Notepad", ClickCaptions.Build(MouseButton.Right, true, "Notepad", El("", "MenuItem")));
    }

    /// <summary>Names go in verbatim: a whitespace name (EDGE-CAP-56) and quotes are neither trimmed nor escaped.</summary>
    [Fact]
    public void NamesAreVerbatim()
    {
        Assert.Equal("Click ' ' button in Word", ClickCaptions.Build(MouseButton.Left, false, "Word", El(" ", "Button")));
        Assert.Equal("Click 'Don't save' button in Word", ClickCaptions.Build(MouseButton.Left, false, "Word", El("Don't save", "Button")));
        Assert.Equal("Click '  Save  ' button in  Word ", ClickCaptions.Build(MouseButton.Left, false, " Word ", El("  Save  ", "Button")));
    }

    [Fact]
    public void TheHotkeyCaptionNamesTheWindow()
    {
        Assert.Equal("Capture: Notepad", ClickCaptions.Hotkey("Notepad"));
        Assert.Equal("Capture: screen", ClickCaptions.Hotkey(null));
        Assert.Equal("Capture: ", ClickCaptions.Hotkey(""));
    }

    [Fact]
    public void TheAppIsScreenWithNoWindow()
    {
        Assert.Equal("screen", ClickCaptions.AppName(null));
        Assert.Equal("notepad.exe", ClickCaptions.AppName("notepad.exe"));
        Assert.Equal("Click in screen", ClickCaptions.Build(MouseButton.Left, false, ClickCaptions.AppName(null), null));
    }

    [Fact]
    public void ArgumentsAreChecked() => Assert.Throws<ArgumentNullException>(() => ClickCaptions.Build(MouseButton.Left, false, null!, null));
}
