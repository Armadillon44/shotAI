using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Shell;
using ShotAI.Platform.Capture;
using Xunit;

namespace ShotAI.App.Tests.Shell;

/// <summary>Spec 03 7.4.5 and 7.6.1: the About dialog's window, texts and button (AC-SHELL-24's automated half).</summary>
public sealed class AboutWindowTests
{
    [Fact]
    public Task TitleTextsIconAndButton() => Sta.RunAsync(() =>
    {
        var detail = AboutText.Detail("10.0.12", "140.0.3485.54", "x64");
        var about = new AboutWindow(Registration(), AboutText.Message("2.0.0"), detail);
        Assert.Equal("About shotAI", about.Title);
        Assert.Equal("shotAI 2.0.0", about.MessageText.Text);
        Assert.Equal(FontWeights.Bold, about.MessageText.FontWeight);
        Assert.Equal(detail, about.DetailText.Text);
        Assert.True(about.DetailText.IsReadOnly);
        Assert.Equal("OK", about.OkButton.Content);
        Assert.True(about.OkButton.IsDefault);
        Assert.True(about.OkButton.IsCancel);
        Assert.Equal((64.0, 64.0), (about.AppIcon.Width, about.AppIcon.Height));
        // The app icon, a resource of the App assembly: its one 128 x 128 frame, drawn at 64.
        Assert.Equal(128, Assert.IsAssignableFrom<BitmapSource>(about.AppIcon.Source).PixelWidth);
    });

    /// <summary>7.6.1: owned by main, centred on it, not resizable, not in the taskbar.</summary>
    [Fact]
    public Task ADialogOfTheMainWindow() => Sta.RunAsync(() =>
    {
        var about = new AboutWindow(Registration(), "m", "d");
        Assert.Equal(ResizeMode.NoResize, about.ResizeMode);
        Assert.False(about.ShowInTaskbar);
        Assert.Equal(WindowStartupLocation.CenterOwner, about.WindowStartupLocation);
        Assert.Equal(SizeToContent.WidthAndHeight, about.SizeToContent);
    });

    /// <summary>The main window's icon is the same resource (2.10.9).</summary>
    [Fact]
    public Task TheMainWindowWearsTheAppIcon() => Sta.RunAsync(() =>
        Assert.Equal(128, Assert.IsAssignableFrom<BitmapSource>(TestMainWindow.Create().Icon).PixelWidth));

    private static WindowRegistration Registration() => new(new OwnWindowRegistry(NullLogger<OwnWindowRegistry>.Instance));
}
