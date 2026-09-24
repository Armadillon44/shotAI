using System.Collections.Concurrent;
using System.Diagnostics;
using ShotAI.Platform.Capture;
using ShotAI.Platform.Tests.Support;
using Xunit;

namespace ShotAI.Platform.Tests.Capture;

/// <summary>
/// ARCHITECTURE PB-1: the time inside <c>LowLevelMouseProc</c>, stamped at entry and exit into a
/// preallocated array over a thousand <c>SendInput</c> clicks, p99 under 50 microseconds. A
/// latency is the machine's, not the code's, so this is not a CI gate: it runs only when asked,
/// with <c>--filter-trait "Category=Perf" --explicit on</c>, on the reference machines.
/// </summary>
[Collection(InputHookCollection.Name)]
public sealed class HookLatencyTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "Perf")]
    public async Task HookP99IsUnder50Microseconds()
    {
        var clicks = new ConcurrentQueue<int>();
        using var target = new ClickTarget();
        using var source = new Win32TriggerSource(new ListLogger<Win32TriggerSource>());
        source.Attach(m => clicks.Enqueue(m.X), null);
        var timings = new HookTimings(4000);
        Win32TriggerSource.TimeHookForTest(timings);
        try
        {
            for (var sent = 0; sent < 1000; sent += 50)
            {
                for (var i = 0; i < 50; i++)
                {
                    var (x, y) = target.Point(i);
                    SyntheticInput.Click(x, y);
                }
                var deadline = DateTime.UtcNow + MouseHookTests.Timeout;
                while (clicks.Count < sent + 50)
                {
                    if (DateTime.UtcNow > deadline) throw new TimeoutException($"{clicks.Count} of {sent + 50} clicks arrived");
                    await Task.Delay(10, TestContext.Current.CancellationToken);
                }
            }
        }
        finally
        {
            Win32TriggerSource.TimeHookForTest(null);
        }

        var micros = timings.Durations().Select(t => t * 1_000_000.0 / Stopwatch.Frequency).Order().ToArray();
        var p99 = micros[(int)(micros.Length * 0.99)];
        TestContext.Current.SendDiagnosticMessage($"hook p99 {p99:F1} us, max {micros[^1]:F1} us, over {micros.Length} calls");
        Assert.True(p99 < 50, $"hook p99 is {p99:F1} us");
    }
}
