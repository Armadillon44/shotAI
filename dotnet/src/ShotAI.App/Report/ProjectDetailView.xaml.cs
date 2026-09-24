using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ShotAI.App.Chrome;

namespace ShotAI.App.Report;

/// <summary>
/// The project view (spec 05 7.9): its data context is the <see cref="ProjectDetailViewModel"/>.
/// Every open starts the report at the top (EDGE-REP-42), where Home keeps its place (06 2.19).
/// </summary>
public partial class ProjectDetailView : UserControl
{
    /// <summary>A project view.</summary>
    public ProjectDetailView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>The report's scroller.</summary>
    internal ScrollViewer ScrollViewer => Scroller;

    /// <summary>The report's frame.</summary>
    internal ReportFrame ReportFrame => Frame;

    /// <summary>The cards.</summary>
    internal ReportList CardList => Cards;

    /// <summary>The report's notices.</summary>
    internal NoticeHost NoticeHost => Notices;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged old) old.PropertyChanged -= OnViewModelChanged;
        if (e.NewValue is INotifyPropertyChanged model) model.PropertyChanged += OnViewModelChanged;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectDetailViewModel.Report)) Scroller.ScrollToVerticalOffset(0);
    }
}
