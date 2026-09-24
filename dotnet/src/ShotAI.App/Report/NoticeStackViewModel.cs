using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using ShotAI.App.Chrome;
using ShotAI.Core.Report;

namespace ShotAI.App.Report;

/// <summary>The report's notice slots, in stack order (spec 05 2.7, 7.16).</summary>
public enum ReportNoticeSlot
{
    /// <summary><c>Import failed: </c> and the message: an image import, a text insert or a screenshot failed.</summary>
    Import,

    /// <summary><c>Export failed: </c> and the message.</summary>
    Export,

    /// <summary><c>Your last change couldn't be saved and was undone. </c> and the message: a write the disk refused was rolled back (7.5).</summary>
    Save,

    /// <summary>Information, with no prefix: the capture guard (EDGE-REP-23).</summary>
    Info,
}

/// <summary>
/// The report's notices (spec 05 7.16): four slots of one message each, shown top to bottom in
/// slot order by 03's notice control, pinned to the top of the report under the command bar
/// (EDGE-REP-31). A new message replaces its slot's; nothing is dismissed but by the user or
/// <see cref="Clear"/> (parity). The same shape as <see cref="NoticeCenter"/>, so one
/// <see cref="NoticeHost"/> renders either. UI thread only.
/// </summary>
public sealed class NoticeStackViewModel : ViewModelBase
{
    private readonly ObservableCollection<NoticeViewModel> _notices = [];
    private readonly Dictionary<ReportNoticeSlot, NoticeViewModel> _shown = [];

    /// <summary>A stack showing nothing.</summary>
    public NoticeStackViewModel()
    {
        Notices = new ReadOnlyObservableCollection<NoticeViewModel>(_notices);
        DismissCommand = new RelayCommand<NoticeViewModel>(Dismiss);
    }

    /// <summary>The notices shown, in slot order.</summary>
    public ReadOnlyObservableCollection<NoticeViewModel> Notices { get; }

    /// <summary>The <c>&#215;</c> button's command, with the notice as its parameter.</summary>
    public ICommand DismissCommand { get; }

    /// <summary>The notice in <paramref name="slot"/>, or null.</summary>
    public NoticeViewModel? In(ReportNoticeSlot slot) => _shown.GetValueOrDefault(slot);

    /// <summary>Shows <paramref name="message"/> in <paramref name="slot"/>, with the slot's prefix, replacing the one there.</summary>
    public void Show(ReportNoticeSlot slot, string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var text = PrefixOf(slot) + message;
        if (_shown.TryGetValue(slot, out var shown))
        {
            shown.Text = text;
            return;
        }
        var notice = new NoticeViewModel(slot == ReportNoticeSlot.Info ? NoticeKind.Info : NoticeKind.Error, text);
        _shown[slot] = notice;
        _notices.Insert(_shown.Keys.Count(s => s < slot), notice);
    }

    /// <summary>Takes <paramref name="slot"/>'s notice down.</summary>
    public void Clear(ReportNoticeSlot slot)
    {
        if (!_shown.Remove(slot, out var notice)) return;
        _notices.Remove(notice);
    }

    private static string PrefixOf(ReportNoticeSlot slot) => slot switch
    {
        ReportNoticeSlot.Import => ReportStrings.ImportFailed,
        ReportNoticeSlot.Export => ReportStrings.ExportFailed,
        ReportNoticeSlot.Save => ReportStrings.RolledBack,
        _ => "",
    };

    private void Dismiss(NoticeViewModel? notice)
    {
        foreach (var (slot, shown) in _shown)
        {
            if (!ReferenceEquals(shown, notice)) continue;
            Clear(slot);
            return;
        }
    }
}
