using ShotAI.Core.Settings;
using Xunit;

namespace ShotAI.App.Tests.Support;

/// <summary>
/// An <see cref="ISettingsService"/> the test drives: <see cref="Set"/> replaces the snapshot and
/// raises <see cref="Changed"/> on the calling thread, as the real service does for its
/// optimistic step; the settings-queue thread is <see cref="SetFromAnotherThreadAsync"/>. A write
/// applies at once, coerced as the real service coerces, and with <see cref="WriteFails"/> set it
/// fails as the real service's does when the file write fails: the change is rolled back with
/// <see cref="SettingsChangedEventArgs.IsRollback"/> and the task faults.
/// </summary>
internal sealed class FakeSettingsService : ISettingsService
{
    private AppSettings _current = SettingsDefaults.Create(@"C:\Users\test\Documents\shotAI");

    /// <summary>When set, reading <see cref="Current"/> throws it.</summary>
    public Exception? CurrentThrows { get; set; }

    /// <summary>When set, each write is applied, then rolled back, and its task faults with it.</summary>
    public Exception? WriteFails { get; set; }

    /// <summary>The writes asked for: <see cref="UpdateAsync"/> calls.</summary>
    public int Writes { get; private set; }

    public AppSettings Current
    {
        get
        {
            if (CurrentThrows is { } ex) throw ex;
            return _current;
        }
    }

    public event EventHandler<SettingsChangedEventArgs>? Changed;

    public string SettingsFilePath => @"C:\Users\test\AppData\Roaming\shotAI\settings.json";

    /// <summary>The subscribers of <see cref="Changed"/>.</summary>
    public int Subscribers => Changed?.GetInvocationList().Length ?? 0;

    /// <summary>Replaces the snapshot and raises <see cref="Changed"/>, a rollback when <paramref name="rollback"/>.</summary>
    public void Set(Func<AppSettings, AppSettings> change, bool rollback = false)
    {
        var previous = _current;
        _current = change(previous);
        Changed?.Invoke(this, new SettingsChangedEventArgs(previous, _current, rollback));
    }

    /// <summary><see cref="Set"/> on a pool thread, as the settings queue raises an outcome.</summary>
    public Task SetFromAnotherThreadAsync(Func<AppSettings, AppSettings> change) =>
        Task.Run(() => Set(change), TestContext.Current.CancellationToken);

    public async Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken ct = default)
    {
        Writes++;
        var previous = _current;
        Set(s => SettingsCoercer.Normalize(change(s), @"C:\Users\test\Documents\shotAI"));
        if (WriteFails is not { } failure) return _current;
        // The outcome comes from the settings queue, after the optimistic step.
        await Task.Yield();
        Set(_ => previous, rollback: true);
        throw failure;
    }

    public Task FlushAsync(TimeSpan timeout) => Task.CompletedTask;
}
