using Microsoft.Win32;
using ShotAI.Core.Theme;

namespace ShotAI.Platform.Theme;

/// <summary>
/// Windows' app mode (spec 06 7.14): the per-user <c>AppsUseLightTheme</c> value Chromium's
/// <c>prefers-color-scheme</c> follows, and its changes, which Windows broadcasts as
/// <c>WM_SETTINGCHANGE</c> with <c>ImmersiveColorSet</c> (<see cref="UserPreferenceCategory.General"/>).
/// </summary>
/// <remarks>
/// <see cref="SystemEvents.UserPreferenceChanged"/> is a static event that keeps its handlers
/// alive, so the monitor is on it only while <see cref="Changed"/> has a handler, and
/// <see cref="Dispose"/> takes it off. <see cref="Changed"/> is raised on the thread
/// <see cref="SystemEvents"/> raises on; subscribers marshal (T6).
/// </remarks>
internal sealed class SystemAppearanceMonitor : ISystemAppearance, IDisposable
{
    /// <summary>The key under <c>HKEY_CURRENT_USER</c>.</summary>
    internal const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>The DWORD: 0 is dark, anything else, or nothing, is light.</summary>
    internal const string AppsUseLightTheme = "AppsUseLightTheme";

    private readonly Func<object?> _read;
    private readonly Lock _lock = new();
    private EventHandler? _changed;
    private bool _hooked;
    private bool _lastDark;
    private bool _disposed;

    /// <summary>The monitor over the user's registry.</summary>
    public SystemAppearanceMonitor()
        : this(ReadRegistry)
    {
    }

    /// <summary>The monitor over <paramref name="read"/>, which returns the raw <c>AppsUseLightTheme</c> value or null.</summary>
    internal SystemAppearanceMonitor(Func<object?> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        _read = read;
    }

    /// <inheritdoc/>
    /// <remarks>Each read is also the value a later change is compared with.</remarks>
    public bool IsDark
    {
        get
        {
            var dark = ReadDark();
            lock (_lock) _lastDark = dark;
            return dark;
        }
    }

    /// <summary>The monitor is on <see cref="SystemEvents.UserPreferenceChanged"/>.</summary>
    internal bool Hooked
    {
        get
        {
            lock (_lock) return _hooked;
        }
    }

    /// <inheritdoc/>
    public event EventHandler? Changed
    {
        // SystemEvents invokes its handlers outside its own lock, so holding this one while
        // hooking or unhooking cannot deadlock with a raise, and a racing add and remove
        // cannot leave the monitor hooked with no handler.
        add
        {
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _changed += value;
                if (_changed is null || _hooked) return;
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
                _hooked = true;
            }
        }
        remove
        {
            lock (_lock)
            {
                _changed -= value;
                if (_changed is not null || !_hooked) return;
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                _hooked = false;
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            if (_hooked) SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _hooked = false;
            _changed = null;
        }
    }

    /// <summary>
    /// A preference changed: in the General category, re-read the value and raise
    /// <see cref="Changed"/> only when it differs from the last one read or reported. Subscribing
    /// reads nothing, so a subscriber's start does no IO (spec 11 7.10 rule 3).
    /// </summary>
    internal void OnPreferenceChanged(UserPreferenceCategory category)
    {
        if (category != UserPreferenceCategory.General) return;
        var dark = ReadDark();
        EventHandler? handlers;
        lock (_lock)
        {
            if (_disposed || dark == _lastDark) return;
            _lastDark = dark;
            handlers = _changed;
        }
        handlers?.Invoke(this, EventArgs.Empty);
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e) => OnPreferenceChanged(e.Category);

    private bool ReadDark() => _read() is int value && value == 0;

    private static object? ReadRegistry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue(AppsUseLightTheme);
    }
}
