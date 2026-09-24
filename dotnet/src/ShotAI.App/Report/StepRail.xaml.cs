using System.Windows.Controls;

namespace ShotAI.App.Report;

/// <summary>A card's rail: its number or callout badge (spec 05 2.9). The data context is the <see cref="StepCardViewModel"/>.</summary>
public partial class StepRail : UserControl
{
    /// <summary>A rail.</summary>
    public StepRail() => InitializeComponent();
}
