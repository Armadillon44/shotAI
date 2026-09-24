using Microsoft.Extensions.Logging;
using ShotAI.Core.Paths;
using ShotAI.Core.Settings;
using ShotAI.Core.Store;

namespace ShotAI.Core.SelfTest;

/// <summary>
/// Builds the settings service and project store a store self-test runs against, over the
/// isolated paths <see cref="StoreSelfTest.RunAsync"/> gives it, so the only settings file they
/// read or write is the test's own (spec 10 7.8 step 2, INV-INFRA-30). The App makes one at
/// startup step 3, before the container exists.
/// </summary>
/// <remarks>
/// The store runs on the managed seams, <see cref="ManagedPathProbe"/> and
/// <see cref="ManagedRenameRetryClassifier"/>, which spec 01 7.14 provides for the self-test:
/// the Windows ones are internal to Platform and reachable only through the container.
/// </remarks>
public sealed class ProjectStoreFactory
{
    private readonly Func<IAppPaths, SelfTestStore> _create;

    /// <param name="time">The clock of the store and the settings service.</param>
    /// <param name="loggers">Their loggers, which write what the same services write in the app.</param>
    public ProjectStoreFactory(TimeProvider time, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(loggers);
        _create = paths => Build(paths, time, loggers);
    }

    /// <summary>For a test that replaces or wraps the store.</summary>
    internal ProjectStoreFactory(Func<IAppPaths, SelfTestStore> create) => _create = create;

    /// <summary>A settings service over <paramref name="paths"/> and a store over it.</summary>
    internal SelfTestStore Create(IAppPaths paths) => _create(paths);

    /// <summary>What the public constructor builds; a test wraps it.</summary>
    internal static SelfTestStore Build(IAppPaths paths, TimeProvider time, ILoggerFactory loggers, Func<string>? newId = null)
    {
        var probe = new ManagedPathProbe();
        var atomic = new AtomicFile(time, new ManagedRenameRetryClassifier());
        var settings = SettingsService.Load(paths, atomic, time, loggers.CreateLogger<SettingsService>());
        var archive = new ArchiveEngine(probe, atomic, loggers.CreateLogger<ArchiveEngine>());
        var store = newId is null
            ? new ProjectStore(settings, probe, atomic, archive, time, loggers.CreateLogger<ProjectStore>())
            : new ProjectStore(settings, probe, atomic, archive, time, loggers.CreateLogger<ProjectStore>(), newId);
        // The store first: one of its jobs may still be writing the recents through the settings.
        return new SelfTestStore(store, store, settings);
    }
}
