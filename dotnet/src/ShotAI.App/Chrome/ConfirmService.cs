using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShotAI.Core.Home;
using ShotAI.Core.Threading;

namespace ShotAI.App.Chrome;

/// <summary>
/// <see cref="IConfirmService"/> (spec 06 7.11): holds the question <see cref="ConfirmHost"/>
/// shows and answers it once. A second question while one is open answers the first false, then
/// shows (IMPROVEMENT D-HOME-13: Electron left the first promise pending forever).
/// </summary>
/// <remarks>A singleton on the UI thread, like <see cref="NoticeCenter"/>.</remarks>
public sealed partial class ConfirmService : ObservableObject, IConfirmService
{
    private readonly IUiDispatcher _ui;

    /// <summary>A service showing nothing.</summary>
    public ConfirmService(IUiDispatcher ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        _ui = ui;
    }

    /// <summary>The question shown, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen))]
    private ConfirmRequest? _current;

    /// <summary>A question is shown.</summary>
    public bool IsOpen => Current is not null;

    /// <inheritdoc/>
    public Task<bool> ConfirmAsync(string message, string confirmLabel = HomeText.ConfirmOk, bool danger = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(confirmLabel);
        return ShowAsync(new ConfirmRequest(message, confirmLabel, danger, alertOnly: false), ct);
    }

    /// <inheritdoc/>
    public Task AlertAsync(string message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return ShowAsync(new ConfirmRequest(message, HomeText.ConfirmOk, danger: false, alertOnly: true), ct);
    }

    /// <summary>The confirm button, and an alert's OK.</summary>
    [RelayCommand]
    private void Confirm()
    {
        if (Current is { } shown) End(shown, true);
    }

    /// <summary>Cancel and Escape.</summary>
    [RelayCommand]
    private void Cancel()
    {
        if (Current is { } shown) End(shown, false);
    }

    private Task<bool> ShowAsync(ConfirmRequest request, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return Task.FromResult(false);
        if (Current is { } open) End(open, false);
        Current = request;
        // The token may be cancelled on any thread; the answer is given on this one.
        if (ct.CanBeCanceled) request.Registration = ct.Register(() => _ui.Post(() => End(request, false)));
        return request.Answer.Task;
    }

    // The question leaves the screen before it is answered, so an awaiter that asks again finds none open.
    private void End(ConfirmRequest request, bool answer)
    {
        if (ReferenceEquals(Current, request)) Current = null;
        request.Registration.Dispose();
        request.Answer.TrySetResult(answer);
    }
}
