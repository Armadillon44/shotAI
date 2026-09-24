using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Shell;
using ShotAI.Core.Threading;
using ShotAI.Platform.Shell;
using Rect = ShotAI.Core.Model.Rect;

namespace ShotAI.App.Shell;

/// <summary>
/// Electron's <c>RegionService</c> and its <c>region:select-area</c> handler (spec 03 2.5.1,
/// 7.4.4; spec 11 2.5.3, 2.5.6): the requester hides, an overlay opens on every monitor, and the
/// first result from one of them, or a cancellation, ends the selection and brings the requester
/// back. UI thread only, so the pending selection needs no lock.
/// </summary>
/// <remarks>
/// Each selection has a generation, and only the pending generation's overlays can end it
/// (INV-SHELL-15, INV-IPC-15), where Electron checked the sender. Its task continues
/// asynchronously, so the caller of a selection that a newer one ended restores the requester
/// only after the newer one is pending, and sees it (EDGE-SHELL-47, D23).
/// </remarks>
public sealed partial class AreaSelectionService : IAreaSelectionService
{
    private readonly WindowRegistration _registration;
    private readonly IUiDispatcher _ui;
    private readonly ILogger<AreaSelectionService> _log;
    private readonly Func<IReadOnlyList<MonitorDescriptorEx>> _monitors;
    private readonly Func<(int X, int Y)> _cursor;
    private Selection? _pending;
    private int _generation;

    /// <summary>The selection over the real monitors and cursor.</summary>
    /// <param name="registration">Registers each overlay, which excludes it from capture before it shows (INV-SHELL-1, INV-SHELL-3).</param>
    /// <param name="ui">The UI thread, where a cancellation ends the selection.</param>
    /// <param name="log">The selection's log lines (7.9).</param>
    public AreaSelectionService(WindowRegistration registration, IUiDispatcher ui, ILogger<AreaSelectionService> log)
        : this(registration, ui, log, MonitorQueries.All, MonitorQueries.CursorPosition)
    {
    }

    /// <summary>The selection over the given monitors and cursor, for tests that stand in for them.</summary>
    internal AreaSelectionService(
        WindowRegistration registration,
        IUiDispatcher ui,
        ILogger<AreaSelectionService> log,
        Func<IReadOnlyList<MonitorDescriptorEx>> monitors,
        Func<(int X, int Y)> cursor)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(cursor);
        _registration = registration;
        _ui = ui;
        _log = log;
        _monitors = monitors;
        _cursor = cursor;
    }

    /// <summary>The pending selection's overlays, in the monitors' order, for the tests; empty when none is pending.</summary>
    internal IReadOnlyList<AreaOverlayWindow> PendingOverlays => _pending?.Overlays ?? [];

    /// <summary>The pending selection's generation, for the tests; 0 when none is pending.</summary>
    internal int PendingGeneration => _pending?.Generation ?? 0;

    /// <inheritdoc/>
    public async Task<Rect?> SelectAreaAsync(Window requester, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(requester);
        if (!_ui.CheckAccess()) throw new InvalidOperationException("An area selection runs on the UI thread.");
        if (ct.IsCancellationRequested) return null;
        // WPF refuses the handle of a window that has closed, so such a requester fails here, before anything changes.
        var hwnd = new WindowInteropHelper(requester).EnsureHandle();
        // Electron's "cancel any in-flight selection": the earlier caller gets null (2.5.1).
        if (_pending is { } earlier) Finish(earlier.Generation, null);
        var closed = false;
        void OnClosed(object? sender, EventArgs e) => closed = true;
        requester.Closed += OnClosed;
        // 11 2.5.3: out of the way of, and out of, the area being selected.
        requester.Hide();
        try
        {
            var selection = new Selection(++_generation, requester);
            _pending = selection;
            // Disposed when the selection ends, so a later cancellation of a long-lived token posts nothing.
            using var cancel = ct.Register(() => _ui.Post(() => Finish(selection.Generation, null)));
            try
            {
                Open(selection);
            }
            catch
            {
                // An overlay that failed to open ends the selection: the ones already open close, and the error is the caller's.
                if (ReferenceEquals(_pending, selection)) _pending = null;
                CloseAll(selection);
                throw;
            }
            return await selection.Result.Task;
        }
        finally
        {
            requester.Closed -= OnClosed;
            // A newer selection for the same requester keeps it hidden; that selection's end brings it back (D23).
            if (!closed && !ReferenceEquals(_pending?.Requester, requester)) Restore(requester, hwnd);
        }
    }

    /// <summary>
    /// Electron's <c>finish</c> and <c>teardown</c> (2.5.1): a result of any generation but the
    /// pending one is ignored (INV-SHELL-15); otherwise the selection ends, every overlay of it
    /// closes, and its task gets the result, once (INV-SHELL-14). Every null result logs the
    /// cancel line (D24).
    /// </summary>
    /// <param name="generation">The generation of the overlay, or cancellation, that ends it.</param>
    /// <param name="result">The rectangle in global physical pixels, or null.</param>
    internal void Finish(int generation, Rect? result)
    {
        if (_pending is not { } selection || selection.Generation != generation) return;
        _pending = null;
        if (result is { } r) Selected(_log, (int)r.Width, (int)r.Height, (int)r.X, (int)r.Y);
        else Cancelled(_log);
        CloseAll(selection);
        selection.Result.TrySetResult(result);
    }

    private static void CloseAll(Selection selection)
    {
        // Each close raises Closed, which finds the selection over already.
        foreach (var overlay in selection.Overlays) overlay.CloseIfOpen();
    }

    // Shown and activated, as Electron's show() and focus(); Windows may refuse the foreground and flash the taskbar button instead (EDGE-SHELL-30).
    private static void Restore(Window requester, nint hwnd)
    {
        requester.Show();
        requester.Activate();
        Foreground.TryActivate(hwnd);
    }

    private void Open(Selection selection)
    {
        foreach (var monitor in _monitors())
        {
            var overlay = new AreaOverlayWindow(_registration, _ui, monitor, result => Finish(selection.Generation, result));
            selection.Overlays.Add(overlay);
            overlay.Show();
        }
        OverlaysOpened(_log, selection.Overlays.Count);
        // Electron would wait for ever with no display; natively the selection ends at once (EDGE-SHELL-50, D22).
        if (selection.Overlays.Count == 0)
        {
            Finish(selection.Generation, null);
            return;
        }
        ActivateUnderCursor(selection.Overlays);
    }

    // Each overlay activated itself as it showed, so the last one has the focus; the one under
    // the cursor takes it, so Esc works where the user is looking (EDGE-SHELL-27, D12).
    private void ActivateUnderCursor(List<AreaOverlayWindow> overlays)
    {
        (int X, int Y) cursor;
        try
        {
            cursor = _cursor();
        }
        catch (Win32Exception)
        {
            return;
        }
        var index = AreaSelectionMath.OverlayUnder([.. overlays.Select(o => o.Monitor.Bounds)], cursor.X, cursor.Y);
        if (index >= 0) overlays[index].Activate();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "region: overlay opened across {Count} display(s)")]
    private static partial void OverlaysOpened(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "region selected: {Width}x{Height} @ ({X},{Y}) [physical px]")]
    private static partial void Selected(ILogger logger, int width, int height, int x, int y);

    [LoggerMessage(Level = LogLevel.Debug, Message = "region: selection cancelled")]
    private static partial void Cancelled(ILogger logger);

    /// <summary>One selection: its generation, the window it hid, its overlays and its result.</summary>
    private sealed class Selection(int generation, Window requester)
    {
        public int Generation { get; } = generation;

        public Window Requester { get; } = requester;

        public List<AreaOverlayWindow> Overlays { get; } = [];

        public TaskCompletionSource<Rect?> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
