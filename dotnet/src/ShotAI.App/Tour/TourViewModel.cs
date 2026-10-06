using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Settings;
using ShotAI.Core.Tour;

namespace ShotAI.App.Tour;

/// <summary>
/// The onboarding tour (spec 06 2.31, 7.8): Electron's <c>tourOpen</c> flag and the step shown.
/// The tour is open from the first run, or a replay, until it is finished; it is shown only while
/// Home is the view on screen, and starts again at the first step each time Home comes back, as
/// Electron's tour unmounted when Home went (EDGE-HOME-33). Finishing writes
/// <c>hasSeenTour = true</c> once per presentation, and nothing ever writes it false
/// (INV-HOME-20). UI thread only; the shell owns it and tells it when Home shows.
/// </summary>
public sealed partial class TourViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly ILogger<TourViewModel> _log;
    private bool _isOpen;
    private bool _homeShowing = true;
    private int _index;

    /// <summary>A closed tour over <paramref name="settings"/>, whose <c>hasSeenTour</c> it reads and writes.</summary>
    public TourViewModel(ISettingsService settings, ILogger<TourViewModel> log)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(log);
        _settings = settings;
        _log = log;
    }

    /// <summary>The tour is open (Electron's <c>tourOpen</c>): shown now if Home is, else when Home next shows.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        private set
        {
            if (SetProperty(ref _isOpen, value)) OnPropertyChanged(nameof(IsShown));
        }
    }

    /// <summary>
    /// Home is the view on screen; the shell sets it with each transition. Leaving Home puts the
    /// tour back to its first step (2.31's last row).
    /// </summary>
    public bool HomeShowing
    {
        get => _homeShowing;
        set
        {
            if (!SetProperty(ref _homeShowing, value)) return;
            OnPropertyChanged(nameof(IsShown));
            if (!value) Index = 0;
        }
    }

    /// <summary>The tour is on screen: open, over Home (<c>showHome &amp;&amp; !showSettings &amp;&amp; tourOpen</c>).</summary>
    public bool IsShown => _isOpen && _homeShowing;

    /// <summary>The step shown, from 0.</summary>
    public int Index
    {
        get => _index;
        private set
        {
            if (!SetProperty(ref _index, value)) return;
            OnPropertyChanged(nameof(Step));
            OnPropertyChanged(nameof(StepLine));
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(IsLast));
            OnPropertyChanged(nameof(PrimaryText));
        }
    }

    /// <summary>The steps, in order.</summary>
    public IReadOnlyList<TourStep> Steps => TourSteps.All;

    /// <summary>The step shown.</summary>
    public TourStep Step => TourSteps.All[_index];

    /// <summary>The step line: <c>Step 2 of 5</c>, upper-cased by the view.</summary>
    public string StepLine => TourText.StepLine(_index, TourSteps.All.Count);

    /// <summary>Back shows: the first step has none.</summary>
    public bool CanGoBack => _index > 0;

    /// <summary>The last step is shown, whose primary button is Done.</summary>
    public bool IsLast => _index == TourSteps.All.Count - 1;

    /// <summary>The primary button's text: Next, or Done on the last step.</summary>
    public string PrimaryText => IsLast ? TourText.Done : TourText.Next;

    /// <summary>
    /// The first run (7.8): after the main window's first frame, the tour opens unless
    /// <c>hasSeenTour</c> is set. A settings read that fails opens nothing, as Electron swallowed
    /// it, and is logged at warning.
    /// </summary>
    public void OpenIfNotSeen()
    {
        bool seen;
        try
        {
            seen = _settings.Current.HasSeenTour;
        }
        catch (Exception e)
        {
            ReadFailed(_log, e);
            return;
        }
        if (!seen) IsOpen = true;
    }

    /// <summary>Settings' <c>Show intro tour</c>: the tour opens at its first step; <c>hasSeenTour</c> is not touched.</summary>
    public void Replay()
    {
        Index = 0;
        IsOpen = true;
    }

    /// <summary>Next, or Right: the next step, or on the last one the tour finishes.</summary>
    [RelayCommand]
    private void Next()
    {
        if (!_isOpen) return;
        if (IsLast) Finish();
        else Index = _index + 1;
    }

    /// <summary>Back, or Left: the step before, never before the first.</summary>
    [RelayCommand]
    private void Back()
    {
        if (_isOpen) Index = Math.Max(0, _index - 1);
    }

    /// <summary>
    /// Skip, Done, Escape or a click outside the bubble: the tour closes and
    /// <c>hasSeenTour = true</c> is written, once for the presentation however many of these come
    /// (INV-HOME-20, EDGE-HOME-34): the first closes it and the rest find it closed.
    /// </summary>
    [RelayCommand]
    private void Finish()
    {
        if (!_isOpen) return;
        IsOpen = false;
        Index = 0;
        _ = WriteSeenAsync();
    }

    // Fire and forget, as Electron's write was: a failure reaches no one but the log (7.14).
    private async Task WriteSeenAsync()
    {
        try
        {
            await _settings.UpdateAsync(s => s with { HasSeenTour = true });
        }
        catch (Exception e)
        {
            WriteFailed(_log, e);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "tour: settings unreadable, no tour (non-fatal):")]
    private static partial void ReadFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "tour: the hasSeenTour write failed (non-fatal):")]
    private static partial void WriteFailed(ILogger logger, Exception exception);
}
