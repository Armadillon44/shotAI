using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ShotAI.App;

/// <summary>
/// The base of every view model (ARCHITECTURE 5.1, spec 11 Q-IPC-17): in a Debug build a view
/// model made off the UI thread, or one that raises a change from another thread, fails at once
/// instead of corrupting a binding later.
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
#if DEBUG
    private const bool DebugBuild = true;
#else
    private const bool DebugBuild = false;
#endif

    private readonly Dispatcher? _dispatcher;

    /// <exception cref="InvalidOperationException">With <see cref="CheckAffinity"/>, the calling thread has no dispatcher.</exception>
    protected ViewModelBase()
    {
        _dispatcher = Dispatcher.FromThread(Thread.CurrentThread);
        if (CheckAffinity && _dispatcher is null)
            throw new InvalidOperationException($"{GetType().Name} must be created on the UI thread.");
    }

    /// <summary>
    /// Whether view models check their thread: on in a Debug build, off in Release, where the
    /// affinity tests turn it on.
    /// </summary>
    internal static bool CheckAffinity { get; set; } = DebugBuild;

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// With <see cref="CheckAffinity"/>, called off the thread that made the view model: the
    /// failure <c>Dispatcher.VerifyAccess</c> reports, with the view model and property named.
    /// </exception>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (CheckAffinity && _dispatcher is not null && !_dispatcher.CheckAccess())
            throw new InvalidOperationException($"{GetType().Name}.{e.PropertyName} changed off the UI thread; marshal with IUiDispatcher.Post.");
        base.OnPropertyChanged(e);
    }
}
