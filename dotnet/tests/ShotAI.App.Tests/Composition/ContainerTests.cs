using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.App.Composition;
using ShotAI.App.Services;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Settings;
using ShotAI.Core.Store;
using ShotAI.Core.Threading;
using ShotAI.Platform;
using ShotAI.Platform.Capture;
using ShotAI.Platform.Imaging;
using Xunit;

namespace ShotAI.App.Tests.Composition;

/// <summary>
/// Spec 11 8.2 and ARCHITECTURE 4.1 (AC-ARCH-2, AC-IPC-17, AC-MODEL-36): the container builds
/// validated, every catalog interface that exists resolves, and the lifetime and disposal rules
/// hold for every registration.
/// </summary>
public sealed partial class ContainerTests
{
    private static readonly Assembly Core = typeof(IProjectService).Assembly;
    private static readonly Assembly PlatformAssembly = typeof(DllSearchHardening).Assembly;
    private static readonly Assembly AppAssembly = typeof(App).Assembly;

    // The view models spec 11 INV-IPC-22 names as singletons.
    private static readonly HashSet<string> SingletonViewModels = ["AppMenuViewModel", "CapturePillViewModel", "CaptureModePickerViewModel"];

    // A catalog interface that exists before the work package that makes it resolvable, with that
    // package. The entry fails the test once the interface resolves, so that package removes it.
    private static readonly Dictionary<string, string> ResolvableFrom = new(StringComparer.Ordinal)
    {
        // CaptureEngine needs every Platform capture seam, the last of which lands in WP-B6 (spec 02 7.1).
        ["ICaptureService"] = "WP-B6",
    };

    /// <summary>The production path of startup step 6 builds, with both validations on.</summary>
    [Fact]
    public Task BuildsWithValidateOnBuild() => Sta.RunAsync(() =>
    {
        using var temp = new TempDir();
        using var settings = SettingsService.Load(
            new TestAppPaths(temp.Root), new AtomicFile(TimeProvider.System, new ManagedRenameRetryClassifier()), TimeProvider.System, NullLogger<SettingsService>.Instance);
        using var provider = ServiceProviderFactory.Build(NullLoggerFactory.Instance, Dispatcher.CurrentDispatcher, settings);
        Assert.Same(settings, provider.GetRequiredService<ISettingsService>());
    });

    /// <summary>A registration whose dependency is missing fails the build, not the first resolve.</summary>
    [Fact]
    public void ValidateOnBuildIsOn()
    {
        var services = new ServiceCollection().AddSingleton<NeedsMissing>();
        Assert.Throws<AggregateException>(() => ServiceProviderFactory.Create(services));
    }

    /// <summary>A singleton that captures a scoped service fails the build too (C3).</summary>
    [Fact]
    public void ValidateScopesIsOn()
    {
        var services = new ServiceCollection().AddScoped<Scoped>().AddSingleton<CapturesScoped>();
        Assert.Throws<AggregateException>(() => ServiceProviderFactory.Create(services));
    }

    /// <summary>
    /// Every interface of ARCHITECTURE 4.4 and spec 11 7.3 that exists in the build resolves. The
    /// ones later work packages add are listed in the output, as is one that exists before its
    /// dependencies do (<c>ResolvableFrom</c>); <c>IProjectSession</c> is made by its factory,
    /// never resolved (INV-IPC-22).
    /// </summary>
    [Fact]
    public Task EveryCatalogInterfaceResolves() => Sta.RunAsync(() =>
    {
        using var c = new TestContainer(Dispatcher.CurrentDispatcher);
        var names = CatalogNames();
        var resolved = new List<string>();
        var pending = new List<string>();
        foreach (var name in names)
        {
            if (name == "IProjectSession")
            {
                Assert.Null(c.Provider.GetService(typeof(IProjectSession)));
                continue;
            }
            var type = FindType(name);
            if (type is null)
            {
                pending.Add(name);
                continue;
            }
            if (ResolvableFrom.TryGetValue(name, out var workPackage))
            {
                Assert.True(c.Provider.GetService(type) is null, $"{name} resolves now: remove its ResolvableFrom entry ({workPackage})");
                pending.Add($"{name} (until {workPackage})");
                continue;
            }
            Assert.True(c.Provider.GetService(type) is not null, $"{name} does not resolve");
            resolved.Add(name);
        }
        TestContext.Current.TestOutputHelper?.WriteLine("resolved: " + string.Join(", ", resolved));
        TestContext.Current.TestOutputHelper?.WriteLine("not in this build yet: " + string.Join(", ", pending));
        Assert.Superset(new HashSet<string> { "IProjectService", "IProjectSessionFactory", "ISettingsService", "IUiDispatcher", "IAppLifetime" }, resolved.ToHashSet());
    });

    /// <summary>R-ARCH-6: one factory instance serves both interfaces.</summary>
    [Fact]
    public Task SettleAndFactoryAreOneInstance() => Sta.RunAsync(() =>
    {
        using var c = new TestContainer(Dispatcher.CurrentDispatcher);
        var factory = c.Provider.GetRequiredService<IProjectSessionFactory>();
        Assert.IsType<ProjectSessionFactory>(factory);
        Assert.Same(factory, c.Provider.GetRequiredService<IProjectSettle>());
        Assert.Same(factory, c.Provider.GetRequiredService<ProjectSessionFactory>());
    });

    /// <summary>C4: every service is a singleton, and resolving it twice gives the same instance.</summary>
    [Fact]
    public Task SingletonsAreSingletons() => Sta.RunAsync(() =>
    {
        using var c = new TestContainer(Dispatcher.CurrentDispatcher);
        foreach (var d in c.Services.Where(d => !d.ServiceType.IsGenericTypeDefinition && !IsViewModel(d)))
        {
            Assert.True(d.Lifetime == ServiceLifetime.Singleton, $"{d.ServiceType.Name} is {d.Lifetime}");
            Assert.Equal(c.Provider.GetServices(d.ServiceType), c.Provider.GetServices(d.ServiceType));
        }
        Assert.Same(c.Provider.GetRequiredService<IAppLifetime>(), c.Provider.GetRequiredService<IAppLifetime>());
    });

    /// <summary>C4, INV-IPC-22: view models are transient, except the named singletons.</summary>
    [Fact]
    public Task ViewModelsAreTransient() => Sta.RunAsync(() =>
    {
        using var c = new TestContainer(Dispatcher.CurrentDispatcher);
        foreach (var d in c.Services.Where(IsViewModel))
        {
            var expected = SingletonViewModels.Contains(d.ServiceType.Name) ? ServiceLifetime.Singleton : ServiceLifetime.Transient;
            Assert.True(d.Lifetime == expected, $"{d.ServiceType.Name} is {d.Lifetime}");
        }
    });

    /// <summary>
    /// C5, AC-MODEL-36: every disposable singleton, <see cref="ProjectStore"/> included, is
    /// <see cref="IDisposable"/>, because the exit disposes the container synchronously and
    /// <c>ServiceProvider.Dispose</c> throws for a service that is only <see cref="IAsyncDisposable"/>.
    /// </summary>
    [Fact]
    public Task NoAsyncOnlyDisposables() => Sta.RunAsync(() =>
    {
        using var c = new TestContainer(Dispatcher.CurrentDispatcher);
        var checkedTypes = new List<string>();
        foreach (var d in c.Services.Where(d => !d.ServiceType.IsGenericTypeDefinition))
        {
            foreach (var instance in c.Provider.GetServices(d.ServiceType))
            {
                if (instance is IAsyncDisposable) Assert.True(instance is IDisposable, $"{instance!.GetType().Name} is only IAsyncDisposable");
                checkedTypes.Add(instance!.GetType().Name);
            }
        }
        Assert.IsAssignableFrom<IDisposable>(Assert.IsType<ProjectStore>(c.Provider.GetRequiredService<IProjectService>()));
        Assert.Contains(nameof(ProjectStore), checkedTypes);
        Assert.Contains(nameof(SettingsService), checkedTypes);
    });

    /// <summary>The check above catches an async-only disposable: the container's own dispose throws for one.</summary>
    [Fact]
    public void AnAsyncOnlyDisposableBreaksTheExit()
    {
        var provider = ServiceProviderFactory.Create(new ServiceCollection().AddSingleton<AsyncOnly>());
        provider.GetRequiredService<AsyncOnly>();
        Assert.Throws<InvalidOperationException>(provider.Dispose);
    }

    /// <summary>Spec 11 7.10 rule 3: step 9 starts each startup in registration order.</summary>
    [Fact]
    public Task StartupsStartInRegistrationOrder() => Sta.RunAsync(() =>
    {
        var started = new List<string>();
        using var c = new TestContainer(Dispatcher.CurrentDispatcher, s => s
            .AddSingleton<IAppStartup>(new RecordingStartup("first", started))
            .AddSingleton<IAppStartup>(new RecordingStartup("second", started))
            .AddSingleton<IAppStartup>(new RecordingStartup("third", started)));
        App.StartAll(c.Provider.GetServices<IAppStartup>());
        Assert.Equal(["first", "second", "third"], started);
    });

    /// <summary>
    /// INV-ARCH-4: a Platform type that implements a Core interface is internal and sealed, and
    /// Platform types are registered only as Core interfaces.
    /// </summary>
    [Fact]
    public Task PlatformSeamsAreInternalAndRegisteredAsCoreInterfaces() => Sta.RunAsync(() =>
    {
        var seams = PlatformAssembly.GetTypes().Where(t => t.GetInterfaces().Any(i => i.Assembly == Core)).ToList();
        Assert.NotEmpty(seams);
        foreach (var t in seams) Assert.True(!t.IsPublic && !t.IsNestedPublic && t.IsSealed, $"{t.FullName} is not internal sealed");
        using var c = new TestContainer(Dispatcher.CurrentDispatcher);
        // The exceptions, public and registered as themselves: the own-window registry, whose
        // registration surface the App calls for every window it shows (spec 02 7.1), and the
        // report's image decoder, which the App's loader calls (spec 05 7.19).
        Type[] asThemselves = [typeof(OwnWindowRegistry), typeof(ReportImageDecoder)];
        foreach (var d in c.Services.Where(d => d.ImplementationType?.Assembly == PlatformAssembly && !asThemselves.Contains(d.ImplementationType)))
            Assert.True(d.ServiceType.Assembly == Core, $"{d.ImplementationType!.Name} is registered as {d.ServiceType.Name}");
        foreach (var t in asThemselves) Assert.Single(c.Services, d => d.ServiceType == t && d.ImplementationType == t);
    });

    /// <summary>The logger factory is not the container's: the exit line is logged after the container is disposed.</summary>
    [Fact]
    public Task TheLoggerFactoryOutlivesTheContainer() => Sta.RunAsync(() =>
    {
        using var logs = new CapturingLoggerProvider();
        var disposed = false;
        var provider = ServiceProviderFactory.Create(new ServiceCollection().AddShotAILogging(new DisposalSpy(logs, () => disposed = true)));
        provider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>();
        provider.Dispose();
        Assert.False(disposed);
    });

    private static bool IsViewModel(ServiceDescriptor d) =>
        typeof(ViewModelBase).IsAssignableFrom(d.ImplementationType ?? d.ServiceType);

    private static Type? FindType(string name) =>
        new[] { Core, PlatformAssembly, AppAssembly }.SelectMany(a => a.GetTypes()).FirstOrDefault(t => t.Name == name && t.DeclaringType is null);

    // The first column of ARCHITECTURE 4.4's table and of spec 11 7.3's.
    private static IReadOnlyList<string> CatalogNames() =>
    [
        .. FirstColumnNames(RepoFiles.ReadText("docs/native/ARCHITECTURE.md"), "### 4.4 The service catalog is the backbone"),
        .. FirstColumnNames(RepoFiles.ReadText("docs/native/spec/11-service-boundary.md"), "### 7.3 Service catalog"),
    ];

    internal static IEnumerable<string> FirstColumnNames(string markdown, string heading)
    {
        var start = markdown.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"no heading {heading}");
        var rows = markdown[start..].Split('\n').SkipWhile(l => !l.StartsWith('|')).TakeWhile(l => l.StartsWith('|')).Skip(2).ToList();
        Assert.NotEmpty(rows);
        var names = rows.SelectMany(r => Backticked().Matches(r.Split('|')[1]).Select(m => m.Groups[1].Value)).Distinct().ToList();
        Assert.NotEmpty(names);
        return names;
    }

    [GeneratedRegex("`([A-Za-z][A-Za-z0-9_]*)`")]
    private static partial Regex Backticked();

    private sealed class Missing;

    private sealed class NeedsMissing(Missing missing)
    {
        public Missing Missing { get; } = missing;
    }

    private sealed class Scoped;

    private sealed class CapturesScoped(Scoped scoped)
    {
        public Scoped Scoped { get; } = scoped;
    }

    private sealed class AsyncOnly : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingStartup(string name, List<string> started) : IAppStartup
    {
        public void Start() => started.Add(name);
    }

    private sealed class DisposalSpy(Microsoft.Extensions.Logging.ILoggerFactory inner, Action onDispose) : Microsoft.Extensions.Logging.ILoggerFactory
    {
        public void AddProvider(Microsoft.Extensions.Logging.ILoggerProvider provider) => inner.AddProvider(provider);

        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => inner.CreateLogger(categoryName);

        public void Dispose() => onDispose();
    }
}
