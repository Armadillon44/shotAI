namespace ShotAI.App.Settings;

/// <summary>
/// One chip of a Settings radio group (spec 06 2.24): Model, Tone, Effort, Theme or Brand. Its
/// chip's <c>IsChecked</c> is bound both ways, so <see cref="IsSelected"/> always holds what the
/// chip shows: a click or an arrow key that checks the chip raises <see cref="Chosen"/>, and its
/// section writes the value. The section puts the stored value back with <see cref="Show"/>,
/// which raises nothing, so a coerced or rolled back write reaches the chip.
/// </summary>
public sealed class SettingsChoice : ViewModelBase
{
    private bool _isSelected;

    /// <summary>A chip for <paramref name="id"/>, unselected.</summary>
    /// <param name="id">The value <c>settings.json</c> holds for it.</param>
    /// <param name="label">The chip's text.</param>
    public SettingsChoice(string id, string label)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(label);
        Id = id;
        Label = label;
    }

    /// <summary>The chip's checked state was set to true from the view: the user chose it.</summary>
    public event EventHandler? Chosen;

    /// <summary>The value <c>settings.json</c> holds for this choice.</summary>
    public string Id { get; }

    /// <summary>The chip's text.</summary>
    public string Label { get; }

    /// <summary>The chip is checked; set by its binding, which raises <see cref="Chosen"/> when it turns on.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value) && value) Chosen?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Shows whether this is the stored value, without raising <see cref="Chosen"/>.</summary>
    internal void Show(bool selected) => SetProperty(ref _isSelected, selected, nameof(IsSelected));
}
