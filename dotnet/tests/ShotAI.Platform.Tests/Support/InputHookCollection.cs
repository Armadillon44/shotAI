using Xunit;

namespace ShotAI.Platform.Tests.Support;

/// <summary>
/// The tests that install the global mouse hook or send synthetic input. They run alone: the
/// hook sees every test's input, and one trigger source at a time can be attached. They hold
/// the desktop's input against the App tests that send it (<see cref="RealInputLock"/>), and
/// run Per-Monitor V2 DPI aware, as the app does.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class InputHookCollection : ICollectionFixture<PerMonitorV2>, ICollectionFixture<RealInputLock>
{
    public const string Name = "Input hook";
}

/// <summary>
/// Makes the test process Per-Monitor V2 DPI aware, as the app's manifest makes the app
/// (spec 02 INV-CAP-26). The test project's own manifest does the same when the tests run from
/// its executable; under <c>dotnet exec</c> this call does it, and when the manifest already
/// did, Windows refuses the call, which changes nothing.
/// </summary>
public sealed class PerMonitorV2
{
    public PerMonitorV2() => User32.SetProcessDpiAwarenessContext(User32.DpiAwarenessContextPerMonitorAwareV2);
}
