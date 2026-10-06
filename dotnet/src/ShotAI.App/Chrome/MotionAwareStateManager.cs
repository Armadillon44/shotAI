using System.Windows;

namespace ShotAI.App.Chrome;

/// <summary>
/// A <see cref="VisualStateManager"/> that plays a template's transitions only while Windows'
/// animations are on (<see cref="SystemParameters.ClientAreaAnimation"/>, the analogue of
/// <c>prefers-reduced-motion</c>, spec 06 7.5): with them off, each state is reached at once.
/// A template sets it on the element that holds its state groups.
/// </summary>
/// <remarks>
/// WPF calls a custom manager for every state a control asks for, those its template does not
/// define included (a button's <c>Normal</c> or <c>Focused</c>), with a null group and state; the
/// default manager ignores those, and so does this one.
/// </remarks>
public sealed class MotionAwareStateManager : VisualStateManager
{
    /// <inheritdoc/>
    protected override bool GoToStateCore(
        FrameworkElement control, FrameworkElement stateGroupsRoot, string stateName, VisualStateGroup group, VisualState state, bool useTransitions) =>
        group is not null && state is not null
        && base.GoToStateCore(control, stateGroupsRoot, stateName, group, state, useTransitions && SystemParameters.ClientAreaAnimation);
}
