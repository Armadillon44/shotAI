using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.Logging;
using ShotAI.App.Services;
using ShotAI.App.Shell;
using ShotAI.Core.Brand;
using ShotAI.Core.Settings;
using ShotAI.Core.Theme;
using ShotAI.Core.Threading;

namespace ShotAI.App.Chrome;

/// <summary>
/// The theme of the app's windows (spec 06 2.32 and 7.5): one merged dictionary built from the
/// brand table for the active brand and the resolved appearance, replaced whole when either
/// changes, so every <c>DynamicResource</c> repaints without a restart or a lost view state
/// (INV-HOME-25). UI thread.
/// </summary>
/// <remarks>
/// It re-applies when the settings' theme or brand change (rollbacks included), when Windows'
/// app mode changes while the theme follows it (the monitor is subscribed only then, as
/// <c>watchSystemTheme</c> is), and when the project view or its pinned brand changes.
/// </remarks>
public sealed partial class ThemeManager : IAppStartup, IDisposable
{
    /// <summary>
    /// The high-contrast mapping (Q-HOME-12, D-HOME-27) waits for the design sign-off, WP-E6;
    /// until then a high-contrast Windows gets the brand's own colours, as today.
    /// </summary>
    internal const bool HighContrastMappingEnabled = false;

    private readonly ISettingsService _settings;
    private readonly ISystemAppearance _system;
    private readonly NavigationState _nav;
    private readonly IUiDispatcher _ui;
    private readonly ILogger<ThemeManager> _log;
    private readonly bool _highContrastMapping;
    private readonly Func<bool> _isHighContrast;
    private ResourceDictionary? _resources;
    private ResourceDictionary? _slot;
    private bool _started;
    private bool _followingSystem;
    private bool _disposed;

    /// <summary>The manager of the app (ARCHITECTURE 4.3).</summary>
    public ThemeManager(ISettingsService settings, ISystemAppearance system, NavigationState nav, IUiDispatcher ui, ILogger<ThemeManager> log)
        : this(settings, system, nav, ui, log, HighContrastMappingEnabled, () => SystemParameters.HighContrast)
    {
    }

    /// <summary>The manager with the high-contrast switch and reading given, for the tests.</summary>
    internal ThemeManager(
        ISettingsService settings, ISystemAppearance system, NavigationState nav, IUiDispatcher ui, ILogger<ThemeManager> log,
        bool highContrastMapping, Func<bool> isHighContrast)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(nav);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(isHighContrast);
        _settings = settings;
        _system = system;
        _nav = nav;
        _ui = ui;
        _log = log;
        _highContrastMapping = highContrastMapping;
        _isHighContrast = isHighContrast;
    }

    /// <summary>The appearance of the dictionary merged now.</summary>
    public Appearance CurrentAppearance { get; private set; }

    /// <summary>The brand of the dictionary merged now.</summary>
    public string CurrentBrand { get; private set; } = BrandPalette.DefaultBrand;

    /// <summary>The dictionary merged now maps to Windows' high-contrast colours.</summary>
    internal bool CurrentHighContrast { get; private set; }

    /// <summary>Raised on the UI thread after the dictionary was replaced.</summary>
    public event EventHandler? ThemeChanged;

    /// <summary>
    /// Startup step 8 (ARCHITECTURE 4.2), before <c>MainWindow.Show()</c>: builds the dictionary
    /// and merges it into <paramref name="app"/>'s resources, so the first frame is themed
    /// (D-HOME-10). Runs once.
    /// </summary>
    public void ApplyInitial(Application app)
    {
        ArgumentNullException.ThrowIfNull(app);
        ApplyInitial(app.Resources);
    }

    /// <summary>
    /// <see cref="ApplyInitial(Application)"/> into any dictionary; the tests use a window's, since
    /// a process can hold one <see cref="Application"/> only.
    /// </summary>
    internal void ApplyInitial(ResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        if (_resources is not null) throw new InvalidOperationException("The theme was applied already.");
        var (appearance, brand, highContrast) = Resolve();
        _slot = Build(appearance, brand, highContrast);
        resources.MergedDictionaries.Add(_slot);
        _resources = resources;
        (CurrentAppearance, CurrentBrand, CurrentHighContrast) = (appearance, brand, highContrast);
        Applied(_log, brand, Wire(appearance));
    }

    /// <summary>
    /// Startup step 9 (ARCHITECTURE 4.3 order, spec 11 7.10 rule 3): subscribes to what the theme
    /// follows. It reads no file and applies nothing.
    /// </summary>
    public void Start()
    {
        if (_started) throw new InvalidOperationException("The theme manager was started already.");
        _started = true;
        _settings.Changed += OnSettingsChanged;
        _nav.Changed += OnNavigationChanged;
        if (_highContrastMapping) SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        FollowSystem(_settings.Current.Theme == ThemePref.System);
    }

    /// <summary>Unsubscribes from everything; a re-apply already posted then does nothing.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settings.Changed -= OnSettingsChanged;
        _nav.Changed -= OnNavigationChanged;
        if (_highContrastMapping) SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        FollowSystem(false);
    }

    // Raised on the thread that wrote and on the settings queue (spec 11 T6, T7): marshal, then
    // re-read the snapshot in the posted action.
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Previous.Theme == e.Current.Theme && e.Previous.Brand == e.Current.Brand) return;
        _ui.Post(Reapply);
    }

    private void OnSystemAppearanceChanged(object? sender, EventArgs e) => _ui.Post(Reapply);

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast)) _ui.Post(Reapply);
    }

    // The navigation state is the UI thread's own, so the new brand is on the view's first frame.
    private void OnNavigationChanged(object? sender, EventArgs e) => Reapply();

    /// <summary>
    /// Reads the settings, the system and the navigation again and applies what they resolve to.
    /// A failure is logged, never shown: nothing the user did failed (7.14).
    /// </summary>
    internal void Reapply()
    {
        if (_disposed || _resources is null) return;
        try
        {
            FollowSystem(_started && _settings.Current.Theme == ThemePref.System);
            Apply(Resolve());
        }
        catch (Exception ex)
        {
            ApplyFailed(_log, ex);
        }
    }

    private (Appearance Appearance, string Brand, bool HighContrast) Resolve()
    {
        var s = _settings.Current;
        var appearance = AppearanceResolver.Resolve(s.Theme, s.Theme == ThemePref.System && _system.IsDark);
        var brand = BrandPalette.CoerceBrand(ActiveBrandResolver.Resolve(_nav.ProjectViewVisible, _nav.ProjectPinnedBrand, s.Brand));
        return (appearance, brand, _highContrastMapping && _isHighContrast());
    }

    // Unchanged: nothing. Else one new dictionary replaces the old in its slot, so its index and
    // every other merged dictionary stay as they are.
    private void Apply((Appearance Appearance, string Brand, bool HighContrast) next)
    {
        if (next.Appearance == CurrentAppearance && next.Brand == CurrentBrand && next.HighContrast == CurrentHighContrast) return;
        var dictionary = Build(next.Appearance, next.Brand, next.HighContrast);
        var merged = _resources!.MergedDictionaries;
        var index = merged.IndexOf(_slot!);
        if (index < 0) merged.Add(dictionary);
        else merged[index] = dictionary;
        _slot = dictionary;
        (CurrentAppearance, CurrentBrand, CurrentHighContrast) = next;
        Applied(_log, next.Brand, Wire(next.Appearance));
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private static ResourceDictionary Build(Appearance appearance, string brand, bool highContrast)
    {
        var tokens = ThemeTokenSet.For(brand, appearance);
        return highContrast ? ThemeResources.BuildHighContrast(tokens) : ThemeResources.Build(tokens);
    }

    // The monitor is subscribed only while the theme follows Windows (parity with watchSystemTheme).
    private void FollowSystem(bool follow)
    {
        if (follow == _followingSystem) return;
        if (follow) _system.Changed += OnSystemAppearanceChanged;
        else _system.Changed -= OnSystemAppearanceChanged;
        _followingSystem = follow;
    }

    // Electron's data-theme values.
    private static string Wire(Appearance appearance) => appearance == Appearance.Dark ? "dark" : "light";

    [LoggerMessage(Level = LogLevel.Debug, Message = "theme: {Brand} {Appearance}")]
    private static partial void Applied(ILogger logger, string brand, string appearance);

    [LoggerMessage(Level = LogLevel.Warning, Message = "theme apply failed (non-fatal):")]
    private static partial void ApplyFailed(ILogger logger, Exception exception);
}
