using CommunityToolkit.Mvvm.Input;

namespace ShotAI.App.Shell;

/// <summary>
/// One row of View, Brand (spec 03 7.4.5): a <c>MenuItem</c> that WPF never checks by itself
/// (<c>IsCheckable</c> false, <c>IsChecked</c> bound one way), whose click runs the menu's
/// <see cref="AppMenuViewModel.ChooseBrandCommand"/> with <see cref="BrandId"/>, the ticked row
/// included. The label and the tick raise a change only when their value changes, so an open
/// submenu is never disturbed (EDGE-SHELL-12). UI thread only.
/// </summary>
public sealed class BrandMenuRowViewModel : ViewModelBase
{
    private string _label;
    private bool _isChecked;

    /// <summary>The row that pins <paramref name="brandId"/>, or with null App default.</summary>
    internal BrandMenuRowViewModel(string? brandId, string label, bool isChecked)
    {
        BrandId = brandId;
        _label = label;
        _isChecked = isChecked;
    }

    /// <summary>The brand the row pins, or null for App default, which clears the pin.</summary>
    public string? BrandId { get; }

    /// <summary>The row's text.</summary>
    public string Label
    {
        get => _label;
        internal set => SetProperty(ref _label, value);
    }

    /// <summary>The row is ticked (INV-SHELL-16).</summary>
    public bool IsChecked
    {
        get => _isChecked;
        internal set => SetProperty(ref _isChecked, value);
    }

    /// <summary>The row's click: the menu's <see cref="AppMenuViewModel.ChooseBrandCommand"/>, with <see cref="BrandId"/> as its parameter.</summary>
    public required IRelayCommand<string?> Command { get; init; }
}
