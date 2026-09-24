using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Shell;
using ShotAI.Platform.Shell;

namespace ShotAI.App.Shell;

/// <summary>
/// Where a second launch reaches this instance (spec 03 7.4.8): a message-only window named for
/// the user, whose hook surfaces the main window when the registered activation message arrives.
/// </summary>
/// <remarks>
/// <see cref="HwndSource"/> does not let the caller name the window class, so the second launch
/// finds the window by its name. Only instances of the same user, session and integrity see each
/// other, which is the lock's scope too.
/// </remarks>
public sealed partial class ActivationListener : IDisposable
{
    private static readonly nint HwndMessage = -3;

    private readonly Action _activate;
    private readonly ILogger<ActivationListener> _log;
    private HwndSource? _source;
    private uint _message;

    /// <summary>A listener that calls <paramref name="activate"/> on the UI thread for each signal.</summary>
    public ActivationListener(Action activate, ILogger<ActivationListener> log)
    {
        ArgumentNullException.ThrowIfNull(activate);
        ArgumentNullException.ThrowIfNull(log);
        _activate = activate;
        _log = log;
    }

    /// <summary>Startup step 11, on the UI thread: creates the window a second launch looks for.</summary>
    /// <param name="sid">The string SID of the process user.</param>
    /// <exception cref="InvalidOperationException">Already started.</exception>
    public void Start(string sid)
    {
        if (_source is not null) throw new InvalidOperationException("The activation listener is already started.");
        _message = WindowMessages.Register(SingleInstanceIdentity.ActivationMessageName);
        _source = new HwndSource(new HwndSourceParameters(SingleInstanceIdentity.ActivationWindowName(sid))
        {
            WindowClassStyle = 0,
            WindowStyle = 0,
            ParentWindow = HwndMessage,
        });
        _source.AddHook(OnMessage);
    }

    /// <summary>Exit step 4: destroys the window, so a later launch finds none.</summary>
    public void Dispose()
    {
        _source?.Dispose();
        _source = null;
    }

    private nint OnMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)msg != _message) return 0;
        handled = true;
        Surfacing(_log);
        _activate();
        return 0;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "second launch: surfacing the main window")]
    private static partial void Surfacing(ILogger logger);
}
