using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using ShotAI.Core.Shell;

namespace ShotAI.App.Shell;

/// <summary>
/// The pill's two animations as <c>toolbar.css</c> runs them (spec 03 2.4.6, 7.4.3): the
/// confirmation flash, a green ring that fades in over 175 ms and out by 700 ms while it thins
/// from 3 to 2 DIP, and the recording dot's pulse, from 1 to 0.3 and back over 1.4 s. CSS eases
/// each keyframe interval of each property, so every segment has its own spline. With Windows'
/// animations off, the setting Chromium maps <c>prefers-reduced-motion</c> to, the flash holds
/// for 490 ms and then fades linearly, and the dot does not pulse.
/// </summary>
internal static class PillAnimations
{
    /// <summary>The flash's opacity peak, 25% of 700 ms (<c>toolbar.css:149-151</c>).</summary>
    internal const double FlashPeakMs = 175;

    /// <summary>How long the reduced-motion flash holds, 70% of 700 ms (<c>toolbar.css:165-172</c>).</summary>
    internal const double ReducedHoldMs = 490;

    /// <summary>The dot's opacity at the middle of its pulse (<c>toolbar.css:306-310</c>).</summary>
    internal const double PulseLowOpacity = 0.3;

    /// <summary>The ring's width when the flash starts.</summary>
    internal const double RingStart = 3;

    /// <summary>The ring's width at its end, and throughout with animations off.</summary>
    internal const double RingEnd = 2;

    /// <summary>CSS <c>ease-out</c>, <c>cubic-bezier(0, 0, 0.58, 1)</c>.</summary>
    internal static KeySpline EaseOut() => new(0, 0, 0.58, 1);

    /// <summary>CSS <c>ease-in-out</c>, <c>cubic-bezier(0.42, 0, 0.58, 1)</c>.</summary>
    internal static KeySpline EaseInOut() => new(0.42, 0, 0.58, 1);

    /// <summary>
    /// Plays the flash on <paramref name="ring"/> from its start, which a new step's flash does
    /// even while the last one runs, and holds its end (CSS <c>forwards</c>): the ring invisible.
    /// </summary>
    internal static void Flash(Border ring, bool animationsOn)
    {
        ArgumentNullException.ThrowIfNull(ring);
        var end = At(ShellConstants.FlashMs);
        var opacity = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };
        if (animationsOn)
        {
            opacity.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, At(0)));
            opacity.KeyFrames.Add(new SplineDoubleKeyFrame(1, At(FlashPeakMs), EaseOut()));
            opacity.KeyFrames.Add(new SplineDoubleKeyFrame(0, end, EaseOut()));
            var width = new ThicknessAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };
            width.KeyFrames.Add(new DiscreteThicknessKeyFrame(new Thickness(RingStart), At(0)));
            width.KeyFrames.Add(new SplineThicknessKeyFrame(new Thickness(RingEnd), end, EaseOut()));
            ring.BeginAnimation(Border.BorderThicknessProperty, width);
        }
        else
        {
            opacity.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, At(0)));
            opacity.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, At(ReducedHoldMs)));
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, end));
            ring.BeginAnimation(Border.BorderThicknessProperty, null);
            ring.BorderThickness = new Thickness(RingEnd);
        }
        ring.BeginAnimation(UIElement.OpacityProperty, opacity);
    }

    /// <summary>Starts the dot's pulse, or stops it, which puts the dot back at full opacity at once.</summary>
    internal static void Pulse(UIElement dot, bool on)
    {
        ArgumentNullException.ThrowIfNull(dot);
        if (!on)
        {
            dot.BeginAnimation(UIElement.OpacityProperty, null);
            return;
        }
        var pulse = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(ShellConstants.PulseMs),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        pulse.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, At(0)));
        pulse.KeyFrames.Add(new SplineDoubleKeyFrame(PulseLowOpacity, At(ShellConstants.PulseMs / 2.0), EaseInOut()));
        pulse.KeyFrames.Add(new SplineDoubleKeyFrame(1, At(ShellConstants.PulseMs), EaseInOut()));
        dot.BeginAnimation(UIElement.OpacityProperty, pulse);
    }

    private static KeyTime At(double milliseconds) => KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds));
}
