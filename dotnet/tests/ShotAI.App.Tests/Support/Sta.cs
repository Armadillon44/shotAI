using System.Windows.Threading;
using ShotAI.Platform;
using Xunit;

namespace ShotAI.App.Tests.Support;

/// <summary>
/// The in-repo STA harness (ARCHITECTURE 12.1, Q-HOME-15): runs a test body on a dedicated STA
/// thread with its own WPF dispatcher and synchronization context, and pumps that dispatcher
/// until the body's task completes. That thread is the test's UI thread.
/// </summary>
/// <remarks>
/// <para>
/// The first use applies <see cref="DllSearchHardening"/> to the test process, so the windows
/// these tests create load WPF's native parts under the same DLL search the app runs with
/// (INV-PKG-16, EDGE-PKG-48).
/// </para>
/// <para>
/// The bodies run one at a time. WPF reads XAML, images and fonts from one resource package per
/// process, and <c>PackagePart.GetStream</c> is not thread-safe: two UI threads loading the same
/// part at once can fail in <c>PackagePart.CleanUpRequestedStreamsList</c> (seen in WP-A16's CI).
/// The app has one UI thread; without the gate the tests would have one per test class.
/// </para>
/// </remarks>
internal static class Sta
{
    /// <summary>A body that has not finished by then fails the test instead of hanging the run.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private static readonly SemaphoreSlim OneUiThreadAtATime = new(1, 1);

    static Sta()
    {
        if (!DllSearchHardening.Apply()) throw new InvalidOperationException("SetDefaultDllDirectories failed in the test process.");
    }

    /// <summary>Runs <paramref name="body"/> on a new UI thread and completes with it.</summary>
    /// <param name="body">The test.</param>
    /// <param name="failOnDispatcherException">
    /// Whether an exception no handler handled on the dispatcher fails the test. The crash
    /// logging tests turn it off to let their own handler see it.
    /// </param>
    public static async Task RunAsync(Func<Task> body, bool failOnDispatcherException = true)
    {
        ArgumentNullException.ThrowIfNull(body);
        await OneUiThreadAtATime.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            await RunOnNewThreadAsync(body, failOnDispatcherException);
        }
        finally
        {
            OneUiThreadAtATime.Release();
        }
    }

    private static Task RunOnNewThreadAsync(Func<Task> body, bool failOnDispatcherException)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            if (failOnDispatcherException)
            {
                dispatcher.UnhandledException += (_, e) =>
                {
                    done.TrySetException(e.Exception);
                    e.Handled = true;
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                };
            }
            _ = dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await body();
                    done.TrySetResult();
                }
                catch (Exception e)
                {
                    done.TrySetException(e);
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            });
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "test UI thread",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
    }

    /// <inheritdoc cref="RunAsync(Func{Task}, bool)"/>
    public static Task RunAsync(Action body) =>
        RunAsync(() =>
        {
            body();
            return Task.CompletedTask;
        });

    /// <summary>A dispatcher thread of its own, for a test that shuts one down; disposing shuts it down.</summary>
    public static DispatcherThread StartDispatcher() => new();

    internal sealed class DispatcherThread : IDisposable
    {
        private readonly Thread _thread;

        public DispatcherThread()
        {
            using var ready = new ManualResetEventSlim();
            Dispatcher? dispatcher = null;
            _thread = new Thread(() =>
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                ready.Set();
                Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "test dispatcher thread",
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            ready.Wait(TestContext.Current.CancellationToken);
            Dispatcher = dispatcher!;
        }

        public Dispatcher Dispatcher { get; }

        public void Dispose()
        {
            Dispatcher.InvokeShutdown();
            _thread.Join(Timeout);
        }
    }
}
