using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.App.Shell;
using ShotAI.App.Threading;
using ShotAI.Core.Shell;
using ShotAI.Core.Threading;
using ShotAI.Platform.Capture;
using ShotAI.Platform.Shell;
using Xunit;
using Rect = ShotAI.Core.Model.Rect;

namespace ShotAI.App.Tests.Support;

/// <summary>
/// An area selection over stood-in monitors, on the calling UI thread: a requester window, the
/// service with its own registry, a dispatcher that counts its posts, and the service's log.
/// </summary>
/// <remarks>
/// The overlays never cover the whole screen: <c>dotnet test</c> runs the Platform tests at the
/// same time, and their topmost windows, which they probe by screen point, fill the runners'
/// screens down to 640 pixels. A stood-in monitor is off every real screen, or the band
/// <see cref="Band"/> above the primary monitor's taskbar, where the pill's tests click too.
/// </remarks>
internal sealed class AreaSelectionHarness : IDisposable
{
    private readonly (int X, int Y) _cursor = SyntheticMouse.Cursor();
    private bool _requesterClosed;

    /// <param name="monitors">The monitors the selection opens its overlays on.</param>
    /// <param name="cursor">Where the service finds the cursor; far from every stood-in monitor when null.</param>
    /// <param name="registration">The registration the overlays use; one over a registry of the harness's own when null.</param>
    public AreaSelectionHarness(IReadOnlyList<MonitorDescriptorEx> monitors, (int X, int Y)? cursor = null, WindowRegistration? registration = null)
    {
        Monitors = monitors;
        Registration = registration ?? new WindowRegistration(Registry);
        Ui = new CountingDispatcher(new WpfUiDispatcher(Dispatcher.CurrentDispatcher));
        Service = new AreaSelectionService(Registration, Ui, new Logger<AreaSelectionService>(Logs), ReadMonitors, () => cursor ?? (-32000, -32000));
        Requester = new ProbeWindow(Registration) { Left = -20000, Top = -20000 };
        Requester.Closed += (_, _) => _requesterClosed = true;
        Requester.Show();
        RequesterHandle = new WindowInteropHelper(Requester).Handle;
    }

    public OwnWindowRegistry Registry { get; } = new(NullLogger<OwnWindowRegistry>.Instance);

    public WindowRegistration Registration { get; }

    public CountingDispatcher Ui { get; }

    public CapturingLoggerProvider Logs { get; } = new();

    public AreaSelectionService Service { get; }

    /// <summary>The window each selection hides, off screen.</summary>
    public ProbeWindow Requester { get; }

    /// <summary>The requester's HWND.</summary>
    public nint RequesterHandle { get; }

    /// <summary>The monitors the next selection reads; a test may change them between selections.</summary>
    public IReadOnlyList<MonitorDescriptorEx> Monitors { get; set; }

    /// <summary>Thrown by the next read of the monitors instead of returning them, when set.</summary>
    public Exception? MonitorsFail { get; set; }

    /// <summary>How many times the service read the monitors.</summary>
    public int MonitorReads { get; private set; }

    /// <summary>The service's log lines, in order.</summary>
    public IReadOnlyList<string> Lines => [.. Logs.Entries.Select(e => e.Message)];

    /// <summary>
    /// A stood-in monitor of the primary monitor's scale, far off every real screen, so its
    /// overlay covers nothing a test elsewhere looks at; <paramref name="index"/> keeps them apart.
    /// </summary>
    public static MonitorDescriptorEx OffScreen(int index, int width = 1600, int height = 900)
    {
        var rect = new PixelRect(-20000 + (index * 4000), -20000, width, height);
        return new MonitorDescriptorEx(0, rect, rect, MonitorQueries.Primary().Scale, false);
    }

    /// <summary>
    /// A stood-in monitor that real input reaches: 480 by 72 pixels above the primary monitor's
    /// taskbar, below the Platform tests' windows, which end 640 pixels down on the runners' 1024
    /// by 768 screens.
    /// </summary>
    public static MonitorDescriptorEx Band()
    {
        var primary = MonitorQueries.Primary();
        var rect = new PixelRect(primary.WorkArea.X + 40, primary.WorkArea.Bottom - 74, 480, 72);
        return new MonitorDescriptorEx(primary.Handle, rect, rect, primary.Scale, true);
    }

    /// <summary>Starts a selection for the requester; it runs until something ends it.</summary>
    public Task<Rect?> SelectAsync(CancellationToken ct = default) => Service.SelectAreaAsync(Requester, ct);

    /// <summary>Starts a selection and waits until its overlays are shown and laid out.</summary>
    public async Task<Task<Rect?>> OpenAsync(CancellationToken ct = default)
    {
        var selection = SelectAsync(ct);
        Assert.True(await TestShell.UntilAsync(() => Service.PendingOverlays.Count == Monitors.Count && Service.PendingOverlays.All(o => o.IsVisible && o.Surface.ActualWidth > 0), 10), "the overlays did not show");
        return selection;
    }

    /// <summary>The selection's result, which must come within ten seconds.</summary>
    public static async Task<Rect?> ResultAsync(Task<Rect?> selection)
    {
        Assert.True(await TestShell.UntilAsync(() => selection.IsCompleted, 10), "the selection did not end");
        return await selection;
    }

    public void Dispose()
    {
        foreach (var overlay in Service.PendingOverlays.ToList()) overlay.CloseIfOpen();
        if (!_requesterClosed) Requester.Close();
        // The cursor goes back where the test found it, off the band, where later tests open their windows.
        if (SyntheticMouse.Cursor() != _cursor) SyntheticMouse.MoveTo(_cursor.X, _cursor.Y);
    }

    private IReadOnlyList<MonitorDescriptorEx> ReadMonitors()
    {
        MonitorReads++;
        if (MonitorsFail is { } failure) throw failure;
        return Monitors;
    }

    /// <summary>An <see cref="IUiDispatcher"/> that counts the posts it forwards.</summary>
    internal sealed class CountingDispatcher(IUiDispatcher inner) : IUiDispatcher
    {
        private int _posts;

        public int Posts => Volatile.Read(ref _posts);

        public bool CheckAccess() => inner.CheckAccess();

        public void Post(Action action)
        {
            Interlocked.Increment(ref _posts);
            inner.Post(action);
        }

        public Task InvokeAsync(Action action, CancellationToken ct = default) => inner.InvokeAsync(action, ct);

        public Task<T> InvokeAsync<T>(Func<T> func, CancellationToken ct = default) => inner.InvokeAsync(func, ct);

        public Task<T> InvokeAsync<T>(Func<Task<T>> func, CancellationToken ct = default) => inner.InvokeAsync(func, ct);

        public Task InvokeAsync(Func<Task> func, CancellationToken ct = default) => inner.InvokeAsync(func, ct);
    }
}
