using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace ShotAI.App.Shell;

/// <summary>
/// The crash handlers of spec 03 7.4.9 (ARCHITECTURE 8.3, INV-SHELL-20): an exception on any
/// thread reaches the log before it ends or disturbs the process.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Install"/> runs first at startup step 1, before the log exists; <see cref="Attach"/>
/// gives the handlers the log right after. The UI-thread handler is the dispatcher's own
/// <see cref="Dispatcher.UnhandledException"/>, the event <c>Application.DispatcherUnhandledException</c>
/// is raised from, so the tests can raise it without an <c>Application</c>.
/// </para>
/// <para>
/// The generic notice of Q-SHELL-16 for a UI-thread exception joins with <c>INoticeService</c>
/// (WP-A16); until then the exception is logged and handled.
/// </para>
/// </remarks>
public sealed partial class CrashLogging : IDisposable
{
    // How long the any-thread handler waits for the log's writer: the sink's own disposal cap.
    private static readonly TimeSpan FlushWait = TimeSpan.FromSeconds(2);

    private Dispatcher? _dispatcher;
    private ILogger? _log;
    private Func<TimeSpan, bool>? _flush;
    private volatile bool _started;

    /// <summary>Hooks the UI thread's dispatcher, the app domain and the task scheduler.</summary>
    /// <exception cref="InvalidOperationException">Already installed.</exception>
    public void Install(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        if (_dispatcher is not null) throw new InvalidOperationException("The crash handlers are already installed.");
        _dispatcher = dispatcher;
        dispatcher.UnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <summary>The log the handlers write to, and the synchronous flush of its sink.</summary>
    public void Attach(ILogger log, Func<TimeSpan, bool> flush)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(flush);
        _flush = flush;
        _log = log;
    }

    /// <summary>
    /// Startup finished: from now on a UI-thread exception is handled and the app carries on, as
    /// Electron's main process did after <c>uncaughtException</c>.
    /// </summary>
    public void StartupCompleted() => _started = true;

    /// <summary>Unhooks what <see cref="Install"/> hooked. The app never calls it; the tests do.</summary>
    public void Dispose()
    {
        if (_dispatcher is null) return;
        _dispatcher.UnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        _dispatcher = null;
    }

    /// <summary>
    /// A UI-thread exception: logged at Error, and handled once startup has completed. One from
    /// <c>OnStartup</c> is not handled, so it ends the process, and the any-thread handler logs
    /// it again with <c>terminating=true</c> and flushes.
    /// </summary>
    /// <returns>Whether the exception is handled.</returns>
    internal bool OnUiThreadException(Exception exception)
    {
        if (_log is { } log) UiThreadException(log, exception);
        return _started;
    }

    /// <summary>Any thread, the process ending: logged at Error, then the log flushed synchronously.</summary>
    internal void OnUnhandled(object exceptionObject, bool isTerminating)
    {
        if (_log is not { } log) return;
        // C# wraps anything thrown that is not an exception (RuntimeWrappedException).
        UnhandledException(log, isTerminating ? "true" : "false", exceptionObject as Exception);
        _flush?.Invoke(FlushWait);
    }

    /// <summary>A faulted task nobody observed: logged at Warning, then marked observed.</summary>
    internal void OnUnobserved(UnobservedTaskExceptionEventArgs e)
    {
        if (_log is { } log) UnobservedTaskException(log, e.Exception);
        e.SetObserved();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) =>
        e.Handled = OnUiThreadException(e.Exception);

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        OnUnhandled(e.ExceptionObject, e.IsTerminating);

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) => OnUnobserved(e);

    [LoggerMessage(Level = LogLevel.Error, Message = "unhandled exception on the UI thread:")]
    private static partial void UiThreadException(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "unhandled exception (terminating={Terminating}):")]
    private static partial void UnhandledException(ILogger logger, string terminating, Exception? exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "unobserved task exception:")]
    private static partial void UnobservedTaskException(ILogger logger, Exception exception);
}
