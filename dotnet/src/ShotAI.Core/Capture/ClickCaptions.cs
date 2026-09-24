using ShotAI.Core.Model;

namespace ShotAI.Core.Capture;

/// <summary>
/// A capture step's automatic caption (spec 02 2.9, <c>src/main/click-caption.ts</c>): the clicked
/// element by name and kind when one resolved, else the window alone. Names go in verbatim, with
/// no escaping and no trimming.
/// </summary>
public static class ClickCaptions
{
    /// <summary>
    /// <c>controlWord</c>: the noun for a UI Automation control type, or an empty string, which
    /// leaves the noun out (a menu item and any other type).
    /// </summary>
    public static string ControlWord(string? controlType) => controlType switch
    {
        "Button" or "SplitButton" => "button",
        "Hyperlink" => "link",
        "CheckBox" => "checkbox",
        "RadioButton" => "option",
        "Tab" or "TabItem" => "tab",
        "Edit" => "field",
        "ComboBox" => "dropdown",
        "ListItem" or "TreeItem" or "DataItem" => "item",
        "Slider" => "slider",
        "Spinner" => "spinner",
        _ => "",
    };

    /// <summary>
    /// <c>buildClickCaption</c>. A named menu item is a selection whatever the menu tracking
    /// said, because the proximity gate can flag a click in a dialog the menu opened; a named
    /// element is a click or a right-click with its noun; with no name, a right-click, then a
    /// menu selection, then a plain click in the app. An empty name counts as none.
    /// </summary>
    public static string Build(MouseButton button, bool isMenuSelect, string appName, StepElement? element)
    {
        ArgumentNullException.ThrowIfNull(appName);
        var name = element?.Name;
        if (!string.IsNullOrEmpty(name))
        {
            if (element!.ControlType == "MenuItem") return "Select '" + name + "' in " + appName;
            var word = ControlWord(element.ControlType);
            var tail = word.Length > 0 ? " " + word : "";
            return button == MouseButton.Right
                ? "Right-click '" + name + "'" + tail + " in " + appName
                : "Click '" + name + "'" + tail + " in " + appName;
        }
        if (button == MouseButton.Right) return "Right-click in " + appName;
        if (isMenuSelect) return "Select from context menu in " + appName;
        return "Click in " + appName;
    }

    /// <summary>A hotkey or screenshot step's caption: <c>Capture: </c> and the foreground window's title, or <c>screen</c> with none (an empty title stays empty).</summary>
    public static string Hotkey(string? windowTitle) => "Capture: " + (windowTitle ?? "screen");

    /// <summary>The app a caption names: the foreground window's, or <c>screen</c> with none.</summary>
    public static string AppName(string? windowApp) => windowApp ?? "screen";
}
