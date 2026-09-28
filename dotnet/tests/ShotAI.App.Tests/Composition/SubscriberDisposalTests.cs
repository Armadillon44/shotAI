using System.Reflection;
using System.Windows.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using ShotAI.App.Threading;
using ShotAI.Core.Settings;
using ShotAI.Core.Store;
using Xunit;

namespace ShotAI.App.Tests.Composition;

/// <summary>
/// Spec 11 Q-IPC-18, 8.2: a disposed view model is in no singleton event's invocation list, so a
/// singleton never keeps a closed view alive. The events that exist so far are
/// <see cref="IProjectService.ProjectsChanged"/>, <see cref="ISettingsService.Changed"/> and the
/// capture engine's four (WP-B9b); the update events join with their service.
/// </summary>
public sealed class SubscriberDisposalTests
{
    [Fact]
    public Task DisposedSubscriberLeavesEveryEvent() => Sta.RunAsync(() =>
    {
        using var c = new TestContainerLite();
        var vm = new Subscriber(c.Store, c.Settings, unsubscribe: true);
        Assert.Contains(vm, Subscribers(c.Store, nameof(IProjectService.ProjectsChanged)));
        Assert.Contains(vm, Subscribers(c.Settings, nameof(ISettingsService.Changed)));
        vm.Dispose();
        Assert.DoesNotContain(vm, Subscribers(c.Store, nameof(IProjectService.ProjectsChanged)));
        Assert.DoesNotContain(vm, Subscribers(c.Settings, nameof(ISettingsService.Changed)));
    });

    /// <summary>
    /// The capture events: the recording panel, the shell and the pill's controller, the engine's
    /// UI subscribers, leave every one of them when disposed, the state follower's handler with them.
    /// </summary>
    [Fact]
    public Task DisposedCaptureSubscribersLeaveEveryCaptureEvent() => Sta.RunAsync(() =>
    {
        var t = new TestShell();
        var ui = new WpfUiDispatcher(Dispatcher.CurrentDispatcher);
        var controller = new RecordingVisibilityController(t.Capture, ui, new CapturePillViewModel(t.Capture, ui, NullLogger<CapturePillViewModel>.Instance));
        controller.Start();
        Assert.Equal(1 + 3 + 3, t.Capture.Subscribers);
        controller.Dispose();
        t.Dispose();
        Assert.Equal(0, t.Capture.Subscribers);
    });

    /// <summary>The check sees a subscriber that forgets to leave.</summary>
    [Fact]
    public Task ALeakIsFound() => Sta.RunAsync(() =>
    {
        using var c = new TestContainerLite();
        var vm = new Subscriber(c.Store, c.Settings, unsubscribe: false);
        vm.Dispose();
        Assert.Contains(vm, Subscribers(c.Store, nameof(IProjectService.ProjectsChanged)));
        Assert.Contains(vm, Subscribers(c.Settings, nameof(ISettingsService.Changed)));
    });

    /// <summary>The targets of a field-like event's handlers, read from its backing field.</summary>
    internal static IReadOnlyList<object?> Subscribers(object source, string eventName)
    {
        for (var t = source.GetType(); t is not null; t = t.BaseType)
        {
            var field = t.GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field is not null) return ((Delegate?)field.GetValue(source))?.GetInvocationList().Select(d => d.Target).ToList() ?? [];
        }
        throw new InvalidOperationException($"{source.GetType().Name}.{eventName} is not a field-like event");
    }

    private sealed class Subscriber : ViewModelBase, IDisposable
    {
        private readonly IProjectService _store;
        private readonly ISettingsService _settings;
        private readonly bool _unsubscribe;

        public Subscriber(IProjectService store, ISettingsService settings, bool unsubscribe)
        {
            _store = store;
            _settings = settings;
            _unsubscribe = unsubscribe;
            store.ProjectsChanged += OnProjectsChanged;
            settings.Changed += OnSettingsChanged;
        }

        public void Dispose()
        {
            if (!_unsubscribe) return;
            _store.ProjectsChanged -= OnProjectsChanged;
            _settings.Changed -= OnSettingsChanged;
        }

        private void OnProjectsChanged(object? sender, EventArgs e) { }

        private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) { }
    }

    // The two singletons with events, over a temp folder.
    private sealed class TestContainerLite : IDisposable
    {
        private readonly TempDir _temp = new();

        public TestContainerLite()
        {
            var paths = new TestAppPaths(_temp.Root);
            var atomic = new AtomicFile(TimeProvider.System, new ManagedRenameRetryClassifier());
            Settings = SettingsService.Load(paths, atomic, TimeProvider.System, NullLogger<SettingsService>.Instance);
            var probe = new ManagedPathProbe();
            Store = new ProjectStore(Settings, probe, atomic, new ArchiveEngine(probe, atomic, NullLogger<ArchiveEngine>.Instance), TimeProvider.System, NullLogger<ProjectStore>.Instance);
        }

        public SettingsService Settings { get; }

        public ProjectStore Store { get; }

        public void Dispose()
        {
            Store.Dispose();
            Settings.Dispose();
            _temp.Dispose();
        }
    }
}
