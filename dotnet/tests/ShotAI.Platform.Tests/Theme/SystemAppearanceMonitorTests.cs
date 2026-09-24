using Microsoft.Win32;
using ShotAI.Platform.Theme;
using Xunit;

namespace ShotAI.Platform.Tests.Theme;

/// <summary>Spec 06 8.4 and 7.14: Windows' app mode through an injected registry reader, and its changes.</summary>
public sealed class SystemAppearanceMonitorTests
{
    [Fact]
    public void ZeroIsDark()
    {
        using var monitor = new SystemAppearanceMonitor(() => 0);
        Assert.True(monitor.IsDark);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void OtherValuesAreLight(int value)
    {
        using var monitor = new SystemAppearanceMonitor(() => value);
        Assert.False(monitor.IsDark);
    }

    /// <summary>A missing key or value, or one that is not a DWORD, is light.</summary>
    [Fact]
    public void MissingOrOddIsLight()
    {
        using var missing = new SystemAppearanceMonitor(() => null);
        using var text = new SystemAppearanceMonitor(() => "0");
        using var qword = new SystemAppearanceMonitor(() => 0L);
        Assert.False(missing.IsDark);
        Assert.False(text.IsDark);
        Assert.False(qword.IsDark);
    }

    /// <summary>The real reader answers what the registry holds.</summary>
    [Fact]
    public void ReadsTheUsersPersonalizeKey()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SystemAppearanceMonitor.PersonalizeKey);
        var expected = key?.GetValue(SystemAppearanceMonitor.AppsUseLightTheme) is int value && value == 0;
        using var monitor = new SystemAppearanceMonitor();
        Assert.Equal(expected, monitor.IsDark);
    }

    /// <summary>Only a General-category change of the value raises <c>Changed</c>, once per change.</summary>
    [Fact]
    public void ChangedOnlyWhenTheValueChanges()
    {
        object? value = 1;
        using var monitor = new SystemAppearanceMonitor(() => value);
        var raised = 0;
        monitor.Changed += (_, _) => raised++;
        Assert.False(monitor.IsDark);

        monitor.OnPreferenceChanged(UserPreferenceCategory.General);
        Assert.Equal(0, raised);

        value = 0;
        monitor.OnPreferenceChanged(UserPreferenceCategory.Color);
        monitor.OnPreferenceChanged(UserPreferenceCategory.Desktop);
        Assert.Equal(0, raised);
        monitor.OnPreferenceChanged(UserPreferenceCategory.General);
        Assert.Equal(1, raised);
        monitor.OnPreferenceChanged(UserPreferenceCategory.General);
        Assert.Equal(1, raised);

        value = null;
        monitor.OnPreferenceChanged(UserPreferenceCategory.General);
        Assert.Equal(2, raised);
    }

    /// <summary>Subscribing reads nothing (a startup does no IO), and a read is the baseline a change is compared with.</summary>
    [Fact]
    public void SubscribingReadsNothing()
    {
        var reads = 0;
        using var monitor = new SystemAppearanceMonitor(() =>
        {
            reads++;
            return 0;
        });
        var raised = 0;
        monitor.Changed += (_, _) => raised++;
        Assert.Equal(0, reads);
        Assert.True(monitor.IsDark);
        monitor.OnPreferenceChanged(UserPreferenceCategory.General);
        Assert.Equal(0, raised);
    }

    /// <summary>The static <c>SystemEvents</c> event holds the monitor only while someone listens.</summary>
    [Fact]
    public void HookedOnlyWhileSubscribed()
    {
        using var monitor = new SystemAppearanceMonitor(() => 1);
        EventHandler first = (_, _) => { };
        EventHandler second = (_, _) => { };
        Assert.False(monitor.Hooked);
        monitor.Changed += first;
        monitor.Changed += second;
        Assert.True(monitor.Hooked);
        monitor.Changed -= first;
        Assert.True(monitor.Hooked);
        monitor.Changed -= second;
        Assert.False(monitor.Hooked);
    }

    [Fact]
    public void DisposeUnhooksAndStopsRaising()
    {
        object? value = 1;
        var monitor = new SystemAppearanceMonitor(() => value);
        var raised = 0;
        monitor.Changed += (_, _) => raised++;
        monitor.Dispose();
        monitor.Dispose();
        Assert.False(monitor.Hooked);
        value = 0;
        monitor.OnPreferenceChanged(UserPreferenceCategory.General);
        Assert.Equal(0, raised);
        Assert.Throws<ObjectDisposedException>(() => monitor.Changed += (_, _) => { });
    }
}
