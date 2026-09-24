using ShotAI.Core.Capture;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>
/// Spec 02 2.12.2 and 8.4: the control type names of <c>control_type_name</c> and the
/// allowlist of <c>is_actionable</c> (<c>native/element-locator/src/lib.rs:48-117</c>, INV-CAP-16).
/// </summary>
public sealed class UiaControlTypesTests
{
    public static TheoryData<int, string> AllNames => new()
    {
        { 50000, "Button" }, { 50001, "Calendar" }, { 50002, "CheckBox" }, { 50003, "ComboBox" },
        { 50004, "Edit" }, { 50005, "Hyperlink" }, { 50006, "Image" }, { 50007, "ListItem" },
        { 50008, "List" }, { 50009, "Menu" }, { 50010, "MenuBar" }, { 50011, "MenuItem" },
        { 50012, "ProgressBar" }, { 50013, "RadioButton" }, { 50014, "ScrollBar" }, { 50015, "Slider" },
        { 50016, "Spinner" }, { 50017, "StatusBar" }, { 50018, "Tab" }, { 50019, "TabItem" },
        { 50020, "Text" }, { 50021, "ToolBar" }, { 50022, "ToolTip" }, { 50023, "Tree" },
        { 50024, "TreeItem" }, { 50025, "Custom" }, { 50026, "Group" }, { 50027, "Thumb" },
        { 50028, "DataGrid" }, { 50029, "DataItem" }, { 50030, "Document" }, { 50031, "SplitButton" },
        { 50032, "Window" }, { 50033, "Pane" }, { 50034, "Header" }, { 50035, "HeaderItem" },
        { 50036, "Table" }, { 50037, "TitleBar" }, { 50038, "Separator" }, { 50039, "SemanticZoom" },
        { 50040, "AppBar" },
    };

    [Theory]
    [MemberData(nameof(AllNames))]
    public void EachIdHasItsName(int id, string name) => Assert.Equal(name, UiaControlTypes.Name(id));

    [Fact]
    public void ThereAre41Names() => Assert.Equal(41, AllNames.Count);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(49999)]
    [InlineData(50041)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void OtherIdsAreUnknown(int id) => Assert.Equal("Unknown", UiaControlTypes.Name(id));

    [Fact]
    public void TheAllowlistIsExactlyTheFifteenLabelTypes()
    {
        int[] expected = [50000, 50002, 50003, 50004, 50005, 50007, 50011, 50013, 50015, 50016, 50018, 50019, 50024, 50029, 50031];
        Assert.Equal(expected, UiaControlTypes.ActionableIds.Order());
        for (var id = 49990; id <= 50050; id++) Assert.Equal(expected.Contains(id), UiaControlTypes.IsActionable(id));
    }

    /// <summary>Their names are often free content: a field's contents or a block of page text (Q-CAP-10 keeps ComboBox and Edit).</summary>
    [Theory]
    [InlineData("Text")]
    [InlineData("Document")]
    [InlineData("Pane")]
    [InlineData("Window")]
    [InlineData("Custom")]
    public void ContentTypesAreNotActionable(string name) => Assert.False(UiaControlTypes.IsActionable(IdOf(name)));

    [Theory]
    [InlineData("ComboBox")]
    [InlineData("Edit")]
    public void ComboBoxAndEditStayActionable(string name) => Assert.True(UiaControlTypes.IsActionable(IdOf(name)));

    private static int IdOf(string name)
    {
        for (var id = UiaControlTypes.FirstId; id < UiaControlTypes.FirstId + 41; id++)
        {
            if (UiaControlTypes.Name(id) == name) return id;
        }
        throw new ArgumentException(name);
    }
}
