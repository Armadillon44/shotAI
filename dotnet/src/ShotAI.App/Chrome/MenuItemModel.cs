using System.Windows.Input;

namespace ShotAI.App.Chrome;

/// <summary>
/// One entry of an <see cref="OverflowMenu"/> (spec 06 2.20, 7.8): a separator, a header or an
/// action, the three kinds of Electron's <c>MenuItem</c>.
/// </summary>
public abstract record MenuItemModel
{
    private protected MenuItemModel()
    {
    }

    /// <summary>A hairline between groups.</summary>
    public static MenuItemModel Separator { get; } = new MenuSeparatorItem();

    /// <summary>A group's heading, upper-cased when drawn, never focusable.</summary>
    public static MenuItemModel Header(string label) => new MenuHeaderItem(label);

    /// <summary>An item that runs <paramref name="command"/> with <paramref name="parameter"/> after the menu closes.</summary>
    /// <param name="label">The item's text.</param>
    /// <param name="command">What it runs.</param>
    /// <param name="parameter">The command's parameter.</param>
    /// <param name="danger">Drawn in the danger colour.</param>
    /// <param name="enabled">False draws it dimmed and skips it; the command's own <c>CanExecute</c> counts too.</param>
    public static MenuItemModel Action(string label, ICommand command, object? parameter = null, bool danger = false, bool enabled = true) =>
        new MenuActionItem(label, command, parameter, danger, enabled);
}

/// <summary>A separator of an <see cref="OverflowMenu"/>.</summary>
public sealed record MenuSeparatorItem : MenuItemModel;

/// <summary>A header of an <see cref="OverflowMenu"/>.</summary>
/// <param name="Label">The heading, before it is upper-cased.</param>
public sealed record MenuHeaderItem(string Label) : MenuItemModel
{
    /// <inheritdoc cref="MenuHeaderItem"/>
    public string Label { get; } = Label ?? throw new ArgumentNullException(nameof(Label));
}

/// <summary>An action of an <see cref="OverflowMenu"/>.</summary>
/// <param name="Label">The item's text.</param>
/// <param name="Command">What it runs.</param>
/// <param name="Parameter">The command's parameter.</param>
/// <param name="Danger">Drawn in the danger colour.</param>
/// <param name="Enabled">False draws it dimmed and skips it.</param>
public sealed record MenuActionItem(string Label, ICommand Command, object? Parameter, bool Danger, bool Enabled) : MenuItemModel
{
    /// <inheritdoc cref="MenuActionItem"/>
    public string Label { get; } = Label ?? throw new ArgumentNullException(nameof(Label));

    /// <inheritdoc cref="MenuActionItem"/>
    public ICommand Command { get; } = Command ?? throw new ArgumentNullException(nameof(Command));
}
