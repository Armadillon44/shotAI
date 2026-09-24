using System.Windows.Controls;
using ShotAI.App.Chrome;
using ShotAI.App.Home;
using ShotAI.App.Report;

namespace ShotAI.App.Shell;

/// <summary>The main window's content (spec 06 7.6): its data context is the <see cref="ShellViewModel"/>.</summary>
public partial class ShellView : UserControl
{
    /// <summary>A shell view.</summary>
    public ShellView() => InitializeComponent();

    /// <summary>The Home view, made once and kept.</summary>
    internal HomeView HomeView => Home;

    /// <summary>The project view, made once and kept.</summary>
    internal ProjectDetailView ProjectView => Project;

    /// <summary>The overlay layer (03 7.4.10), above the header and the views.</summary>
    internal Grid Overlay => OverlayLayer;

    /// <summary>The notices in the overlay layer.</summary>
    internal NoticeHost NoticeHost => Notices;
}
