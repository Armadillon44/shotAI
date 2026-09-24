using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Errors;

namespace ShotAI.App.Chrome;

/// <summary>
/// <see cref="INoticeService"/> (spec 06 7.10, ARCHITECTURE 5.5): the main window's notices, in
/// stack order, for its <see cref="NoticeHost"/>. The error slot holds one notice at a time; a
/// newer error replaces the text of the one shown. Only a dismiss, a newer error or
/// <see cref="ClearError"/> takes it down: a background refresh never does (D-HOME-3).
/// </summary>
/// <remarks>A singleton on the UI thread. The update slot, below the error, joins in WP-E1.</remarks>
public sealed partial class NoticeCenter : INoticeService
{
    private readonly ILogger<NoticeCenter> _log;
    private readonly ObservableCollection<NoticeViewModel> _notices = [];
    private NoticeViewModel? _error;

    /// <summary>A center showing nothing.</summary>
    public NoticeCenter(ILogger<NoticeCenter> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
        Notices = new ReadOnlyObservableCollection<NoticeViewModel>(_notices);
        DismissCommand = new RelayCommand<NoticeViewModel>(Dismiss);
    }

    /// <summary>The notices shown, top first: the error, then (WP-E1) the update.</summary>
    public ReadOnlyObservableCollection<NoticeViewModel> Notices { get; }

    /// <summary>The <c>&#215;</c> button's command, with the notice as its parameter.</summary>
    public ICommand DismissCommand { get; }

    /// <summary>The error notice shown, or null.</summary>
    public NoticeViewModel? Error => _error;

    /// <inheritdoc/>
    public void ShowError(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (_error is { } shown)
        {
            shown.Text = message;
            return;
        }
        _error = new NoticeViewModel(NoticeKind.Error, message);
        _notices.Insert(0, _error);
    }

    /// <inheritdoc/>
    public void ShowError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var text = UserMessage.From(exception);
        if (text is null) return;
        if (UserMessage.IsUnexpected(exception)) UnexpectedError(_log, exception);
        ShowError(text);
    }

    /// <inheritdoc/>
    public void ClearError()
    {
        if (_error is null) return;
        _notices.Remove(_error);
        _error = null;
    }

    private void Dismiss(NoticeViewModel? notice)
    {
        if (notice is null) return;
        if (ReferenceEquals(notice, _error)) ClearError();
        else _notices.Remove(notice);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "notice: unexpected error, shown as the generic message:")]
    private static partial void UnexpectedError(ILogger logger, Exception exception);
}
