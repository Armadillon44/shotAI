using System.Windows;
using System.Windows.Controls;
using ShotAI.App.Shell;

namespace ShotAI.App.Tests.Support;

/// <summary>A shotAI window with the controls whose popups the registration tests open.</summary>
internal sealed class ProbeWindow : ShotAIWindow
{
    public ProbeWindow(WindowRegistration registration)
        : base(registration)
    {
        Width = 320;
        Height = 240;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 40;
        Top = 40;
        Button = new Button { Content = "button" };
        Combo = new ComboBox { ItemsSource = new[] { "one", "two", "three" }, SelectedIndex = 0 };
        Content = new StackPanel { Children = { Button, Combo } };
    }

    public Button Button { get; }

    public ComboBox Combo { get; }
}
