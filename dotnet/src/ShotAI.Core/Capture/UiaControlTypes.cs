namespace ShotAI.Core.Capture;

/// <summary>
/// UI Automation control types as the element locator reports them (spec 02 2.12.2,
/// <c>native/element-locator/src/lib.rs:48-117</c>): their names, and the allowlist of types
/// whose name is a label and may appear in a caption (INV-CAP-16).
/// </summary>
public static class UiaControlTypes
{
    /// <summary>The first control type id, <c>UIA_ButtonControlTypeId</c>.</summary>
    public const int FirstId = 50000;

    // Ids 50000 to 50040, in order (control_type_name).
    private static readonly string[] Names =
    [
        "Button", "Calendar", "CheckBox", "ComboBox", "Edit", "Hyperlink", "Image", "ListItem", "List", "Menu",
        "MenuBar", "MenuItem", "ProgressBar", "RadioButton", "ScrollBar", "Slider", "Spinner", "StatusBar", "Tab", "TabItem",
        "Text", "ToolBar", "ToolTip", "Tree", "TreeItem", "Custom", "Group", "Thumb", "DataGrid", "DataItem",
        "Document", "SplitButton", "Window", "Pane", "Header", "HeaderItem", "Table", "TitleBar", "Separator", "SemanticZoom",
        "AppBar",
    ];

    // is_actionable: the 15 label-bearing types. Text, Document and Pane are left out on purpose,
    // because their name is often free content (a field's contents or a block of page text).
    private static readonly HashSet<int> Actionable =
    [
        50000, // Button
        50002, // CheckBox
        50003, // ComboBox
        50004, // Edit: its name is the field label
        50005, // Hyperlink
        50007, // ListItem
        50011, // MenuItem
        50013, // RadioButton
        50015, // Slider
        50016, // Spinner
        50018, // Tab
        50019, // TabItem
        50024, // TreeItem
        50029, // DataItem
        50031, // SplitButton
    ];

    /// <summary>The allowlist's ids.</summary>
    public static IReadOnlySet<int> ActionableIds => Actionable;

    /// <summary>The name of control type <paramref name="id"/>, or <c>Unknown</c> outside 50000 to 50040.</summary>
    public static string Name(int id) =>
        id >= FirstId && id - FirstId < Names.Length ? Names[id - FirstId] : "Unknown";

    /// <summary>Whether an element of type <paramref name="id"/> may name itself in a caption.</summary>
    public static bool IsActionable(int id) => Actionable.Contains(id);
}
