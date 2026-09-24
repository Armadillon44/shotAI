using System.Runtime.CompilerServices;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.App.Chrome;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Errors;
using Xunit;

namespace ShotAI.App.Tests.Shell;

/// <summary>
/// Spec 03 7.4.9 and 8.3, INV-SHELL-20 (AC-SHELL-25's forced exception): an exception on the
/// UI thread, on a pool thread through an unobserved task, or anywhere the process ends,
/// reaches the log.
/// </summary>
[Collection(CrashLoggingCollection.Name)]
public sealed class CrashLoggingTests
{
    /// <summary>After startup, a UI-thread exception is logged at Error and handled: the dispatcher goes on.</summary>
    [Fact]
    public Task DispatcherExceptionLoggedAndHandled() => Sta.RunAsync(async () =>
    {
        using var logs = new CapturingLoggerProvider();
        using var crash = new CrashLogging();
        crash.Install(Dispatcher.CurrentDispatcher);
        crash.Attach(logs.CreateLogger("crash"), _ => true);
        crash.StartupCompleted();
        var boom = new InvalidOperationException("boom");

        _ = Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => throw boom));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

        var line = Assert.Single(logs.Entries);
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.Equal("unhandled exception on the UI thread:", line.Message);
        Assert.Same(boom, line.Exception);
    }, failOnDispatcherException: false);

    /// <summary>Before startup completes, the exception is logged but not handled, so it ends the process.</summary>
    [Fact]
    public void ExceptionDuringStartupIsNotHandled()
    {
        using var logs = new CapturingLoggerProvider();
        using var crash = new CrashLogging();
        crash.Attach(logs.CreateLogger("crash"), _ => true);
        Assert.False(crash.OnUiThreadException(new InvalidOperationException("startup")));
        crash.StartupCompleted();
        Assert.True(crash.OnUiThreadException(new InvalidOperationException("later")));
        Assert.Equal(2, logs.Entries.Count(e => e.Message == "unhandled exception on the UI thread:"));
    }

    /// <summary>
    /// Q-SHELL-16: after startup, a handled UI-thread exception also shows the generic notice while
    /// the main window is visible; not during startup, when it ends the process, and not while the
    /// window is hidden.
    /// </summary>
    [Fact]
    public Task GenericNoticeAfterStartupWhileVisible() => Sta.RunAsync(() =>
    {
        using var logs = new CapturingLoggerProvider();
        using var crash = new CrashLogging();
        var notices = new NoticeCenter(NullLogger<NoticeCenter>.Instance);
        var visible = true;
        crash.Attach(logs.CreateLogger("crash"), _ => true);
        crash.AttachNotices(notices, () => visible);

        Assert.False(crash.OnUiThreadException(new InvalidOperationException("startup")));
        Assert.Null(notices.Error);

        crash.StartupCompleted();
        visible = false;
        Assert.True(crash.OnUiThreadException(new InvalidOperationException("hidden")));
        Assert.Null(notices.Error);

        visible = true;
        Assert.True(crash.OnUiThreadException(new InvalidOperationException("Sequence contains no elements")));
        Assert.Equal(UserMessage.Generic, notices.Error?.Text);
        Assert.Equal(3, logs.Entries.Count(e => e.Message == "unhandled exception on the UI thread:"));
        Assert.DoesNotContain(logs.Entries, e => e.Message.StartsWith("notice:", StringComparison.Ordinal));
    });

    /// <summary>With no notices attached, a handled exception is only logged (the startup steps before step 8).</summary>
    [Fact]
    public void WithoutNoticesOnlyTheLog()
    {
        using var logs = new CapturingLoggerProvider();
        using var crash = new CrashLogging();
        crash.Attach(logs.CreateLogger("crash"), _ => true);
        crash.StartupCompleted();
        Assert.True(crash.OnUiThreadException(new InvalidOperationException("later")));
        Assert.Single(logs.Entries);
        Assert.Throws<ArgumentNullException>(() => crash.AttachNotices(null!, () => true));
        Assert.Throws<ArgumentNullException>(() => crash.AttachNotices(new NoticeCenter(NullLogger<NoticeCenter>.Instance), null!));
    }

    /// <summary>A faulted task nobody observes is logged at Warning when it is collected, and marked observed.</summary>
    [Fact]
    public void UnobservedTaskLogged()
    {
        using var logs = new CapturingLoggerProvider();
        using var crash = new CrashLogging();
        using var other = Sta.StartDispatcher();
        crash.Install(other.Dispatcher);
        crash.Attach(logs.CreateLogger("crash"), _ => true);

        var marker = "unobserved " + Guid.NewGuid();
        FaultATaskAndDropIt(marker);
        for (var i = 0; i < 10 && !Logged(logs, marker); i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        var line = Assert.Single(logs.Entries, e => Logged(e, marker));
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Equal("unobserved task exception:", line.Message);
    }

    /// <summary>The unobserved-task handler marks the exception observed, so it never ends the process.</summary>
    [Fact]
    public void UnobservedTaskIsMarkedObserved()
    {
        using var logs = new CapturingLoggerProvider();
        using var crash = new CrashLogging();
        crash.Attach(logs.CreateLogger("crash"), _ => true);
        var e = new UnobservedTaskExceptionEventArgs(new AggregateException(new IOException("pool")));
        crash.OnUnobserved(e);
        Assert.True(e.Observed);
        Assert.Equal("unobserved task exception:", Assert.Single(logs.Entries).Message);
    }

    /// <summary>The any-thread handler logs with <c>terminating=</c> and flushes the log synchronously.</summary>
    [Theory]
    [InlineData(true, "unhandled exception (terminating=true):")]
    [InlineData(false, "unhandled exception (terminating=false):")]
    public void AnyThreadExceptionLoggedAndFlushed(bool terminating, string expected)
    {
        using var logs = new CapturingLoggerProvider();
        using var crash = new CrashLogging();
        var flushes = new List<TimeSpan>();
        crash.Attach(logs.CreateLogger("crash"), t => { flushes.Add(t); return true; });
        var boom = new InvalidOperationException("thread");
        crash.OnUnhandled(boom, terminating);
        var line = Assert.Single(logs.Entries);
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.Equal(expected, line.Message);
        Assert.Same(boom, line.Exception);
        Assert.Equal([TimeSpan.FromSeconds(2)], flushes);
    }

    /// <summary>Before the log exists, the handlers write nothing and throw nothing.</summary>
    [Fact]
    public void BeforeTheLogNothingIsWritten()
    {
        using var crash = new CrashLogging();
        Assert.False(crash.OnUiThreadException(new InvalidOperationException()));
        crash.OnUnhandled(new InvalidOperationException(), isTerminating: true);
        var e = new UnobservedTaskExceptionEventArgs(new AggregateException());
        crash.OnUnobserved(e);
        Assert.True(e.Observed);
    }

    [Fact]
    public void InstallsOnce()
    {
        using var other = Sta.StartDispatcher();
        using var crash = new CrashLogging();
        crash.Install(other.Dispatcher);
        Assert.Throws<InvalidOperationException>(() => crash.Install(other.Dispatcher));
        Assert.Throws<ArgumentNullException>(() => new CrashLogging().Install(null!));
        Assert.Throws<ArgumentNullException>(() => crash.Attach(null!, _ => true));
        Assert.Throws<ArgumentNullException>(() => crash.Attach(new CapturingLoggerProvider().CreateLogger("x"), null!));
    }

    private static bool Logged(CapturingLoggerProvider logs, string marker) => logs.Entries.Any(e => Logged(e, marker));

    private static bool Logged(LogEntry e, string marker) =>
        e.Exception is AggregateException a && a.InnerExceptions.Any(x => x.Message == marker);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FaultATaskAndDropIt(string marker)
    {
        var task = Task.Run(() => throw new InvalidOperationException(marker));
        ((IAsyncResult)task).AsyncWaitHandle.WaitOne();
    }
}

/// <summary>The app-domain and task-scheduler handlers are process-wide, so these tests run alone.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CrashLoggingCollection
{
    public const string Name = "crash logging";
}
