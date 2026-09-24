namespace ShotAI.App.Tests.Support;

/// <summary>A store whose only working member is <c>FlushAsync</c>, which runs the function the test gives it.</summary>
internal sealed class FlushingProjectService(Func<TimeSpan, Task> flush) : FakeProjectService
{
    public override Task FlushAsync(TimeSpan timeout) => flush(timeout);
}
