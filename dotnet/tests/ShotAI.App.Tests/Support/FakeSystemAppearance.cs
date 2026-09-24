using ShotAI.Core.Theme;
using Xunit;

namespace ShotAI.App.Tests.Support;

/// <summary>An <see cref="ISystemAppearance"/> whose app mode the test sets and whose change it raises.</summary>
internal sealed class FakeSystemAppearance : ISystemAppearance
{
    private bool _dark;
    private EventHandler? _changed;

    /// <summary>How many times <see cref="IsDark"/> was read.</summary>
    public int Reads { get; private set; }

    /// <summary>How many handlers are subscribed to <see cref="Changed"/>.</summary>
    public int Subscribers => _changed?.GetInvocationList().Length ?? 0;

    public bool IsDark
    {
        get
        {
            Reads++;
            return _dark;
        }
    }

    public event EventHandler? Changed
    {
        add => _changed += value;
        remove => _changed -= value;
    }

    /// <summary>Sets the app mode and raises <see cref="Changed"/>, on the calling thread or, with <paramref name="elsewhere"/>, on a pool thread.</summary>
    public Task SetAsync(bool dark, bool elsewhere = false)
    {
        _dark = dark;
        if (!elsewhere)
        {
            _changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
        return Task.Run(() => _changed?.Invoke(this, EventArgs.Empty), TestContext.Current.CancellationToken);
    }
}
