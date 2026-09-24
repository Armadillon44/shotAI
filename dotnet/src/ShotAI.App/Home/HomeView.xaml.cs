using System.Windows.Controls;
using ShotAI.App.Shell;

namespace ShotAI.App.Home;

/// <summary>
/// The Home view (spec 06 7.6). It is made once and kept, hidden, while another view shows, so its
/// scroller keeps its place; the offset is recorded while it shows and restored when it shows
/// again (2.19, INV-HOME-19).
/// </summary>
public partial class HomeView : UserControl
{
    /// <summary>A Home view; its data context is a <see cref="HomeViewModel"/>.</summary>
    public HomeView()
    {
        InitializeComponent();
        ScrollMemory = new ScrollMemory(Scroller);
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) ScrollMemory.Enter();
            else ScrollMemory.Leave();
        };
    }

    /// <summary>The view's scroller, for the scroll tests.</summary>
    internal ScrollViewer ScrollViewer => Scroller;

    /// <summary>The scroller's memory across views.</summary>
    internal ScrollMemory ScrollMemory { get; }
}
