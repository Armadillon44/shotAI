using System.Windows.Threading;

namespace ShotAI.Platform.Tests.Support;

/// <summary>
/// An STA thread running a WPF dispatcher, standing in for the app's UI thread: it owns the
/// windows it creates, and a test reaches it with <see cref="Invoke{T}"/>. <see cref="Block"/>
/// stops it without pumping, to show a call from another thread does not need it.
/// </summary>
internal sealed class UiThread : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private Dispatcher? _dispatcher;

    public UiThread(string name = "test UI thread")
    {
        _thread = new Thread(() =>
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = name,
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_ready.Wait(Bound)) throw new TimeoutException("The UI thread did not start.");
    }

    public int ManagedThreadId => _thread.ManagedThreadId;

    /// <summary>Runs <paramref name="work"/> on the thread and returns its result.</summary>
    public T Invoke<T>(Func<T> work) => _dispatcher!.Invoke(work, DispatcherPriority.Send, CancellationToken.None, Bound);

    /// <summary>Runs <paramref name="work"/> on the thread.</summary>
    public void Invoke(Action work) => _dispatcher!.Invoke(work, DispatcherPriority.Send, CancellationToken.None, Bound);

    /// <summary>
    /// Blocks the thread in a sleep loop, which pumps no message, until the result is disposed;
    /// returns once it is blocked.
    /// </summary>
    public IDisposable Block()
    {
        var block = new Blocked();
        _ = _dispatcher!.BeginInvoke(block.Run);
        if (!block.Entered.Wait(Bound)) throw new TimeoutException("The UI thread did not block.");
        return block;
    }

    public void Dispose()
    {
        _dispatcher?.InvokeShutdown();
        _thread.Join(Bound);
        _ready.Dispose();
    }

    private sealed class Blocked : IDisposable
    {
        private volatile bool _released;

        public ManualResetEventSlim Entered { get; } = new();

        public void Run()
        {
            Entered.Set();
            var deadline = DateTime.UtcNow + Bound;
            while (!_released && DateTime.UtcNow < deadline) Thread.Sleep(5);
        }

        public void Dispose() => _released = true;
    }
}
