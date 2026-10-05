namespace ShotAI.App.Settings;

/// <summary>Settings' tabs (spec 06 2.24, <c>SETTINGS_TABS</c>), in tab-bar order, which is also the arrow keys' cycle.</summary>
public enum SettingsTab
{
    /// <summary>AI, the tab each open starts on.</summary>
    Ai,

    /// <summary>Capture.</summary>
    Capture,

    /// <summary>Appearance.</summary>
    Appearance,

    /// <summary>Storage.</summary>
    Storage,

    /// <summary>About.</summary>
    About,
}
