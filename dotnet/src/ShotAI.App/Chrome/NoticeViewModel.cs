using CommunityToolkit.Mvvm.ComponentModel;

namespace ShotAI.App.Chrome;

/// <summary>
/// One notice of a notice host (spec 06 7.10): its kind and its text. Replacing the text of the
/// notice shown keeps the notice, as Electron's element stayed mounted while its error changed.
/// </summary>
public sealed partial class NoticeViewModel : ViewModelBase
{
    /// <summary>A notice of <paramref name="kind"/> reading <paramref name="text"/>.</summary>
    public NoticeViewModel(NoticeKind kind, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Kind = kind;
        _text = text;
    }

    /// <summary>The kind, fixed for the notice's life.</summary>
    public NoticeKind Kind { get; }

    /// <summary>The text shown.</summary>
    [ObservableProperty]
    private string _text;
}
