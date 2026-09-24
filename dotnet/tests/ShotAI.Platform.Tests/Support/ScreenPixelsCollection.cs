using Xunit;

namespace ShotAI.Platform.Tests.Support;

/// <summary>
/// The tests that read the screen's pixels with windows of their own on it. They run alone, so
/// no other test's window covers theirs, and Per-Monitor V2 DPI aware, as the app does.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ScreenPixelsCollection : ICollectionFixture<PerMonitorV2>
{
    public const string Name = "Screen pixels";
}
