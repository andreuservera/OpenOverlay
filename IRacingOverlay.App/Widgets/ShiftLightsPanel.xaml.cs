using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

/// <summary>LED strip of 14 segments — 5 green, 5 yellow, 4 red. Separate from ShiftGearPanel (the
/// gear digit itself) because the strip spans the whole speed/gear/RPM cluster.</summary>
public partial class ShiftLightsPanel : UserControl
{
    private const int GreenCount = 5;
    private const int YellowCount = 5;
    private const int BlinkHalfPeriodMs = 90;

    private static readonly Brush Off = StatePalette.LampOff;
    private static readonly Brush Green = StatePalette.Positive;
    private static readonly Brush Yellow = StatePalette.Accent;
    private static readonly Brush Red = StatePalette.Critical;

    // Driven by WPF's own animation clock rather than a phase flag sampled once per telemetry tick:
    // at the default 100ms refresh a 150ms half-period aliased into an uneven on-on-off pattern that
    // read as an occasional flicker rather than a blink, and changing the refresh rate changed the
    // pattern. An animation runs at render frequency, so the cadence is the same at any refresh rate.
    private static readonly DoubleAnimationUsingKeyFrames BlinkAnimation = CreateBlinkAnimation();

    private readonly Border[] _segments = new Border[CockpitState.ShiftLightCount];
    private bool _blinking;

    public ShiftLightsPanel()
    {
        InitializeComponent();

        for (var i = 0; i < _segments.Length; i++)
        {
            var segment = new Border { Width = 15, Height = 7, CornerRadius = new CornerRadius(2), Background = Off, Margin = new Thickness(1, 0, 1, 0) };
            LightsStack.Children.Add(segment);
            _segments[i] = segment;
        }
    }

    public void SetLit(int litCount, bool blink)
    {
        for (var i = 0; i < _segments.Length; i++)
        {
            _segments[i].Background = i >= litCount
                ? Off
                : i < GreenCount ? Green : i < GreenCount + YellowCount ? Yellow : Red;
        }

        if (blink == _blinking)
        {
            return;
        }

        _blinking = blink;
        // Passing null detaches the animation and restores the underlying Opacity.
        LightsStack.BeginAnimation(OpacityProperty, blink ? BlinkAnimation : null);
    }

    private static DoubleAnimationUsingKeyFrames CreateBlinkAnimation()
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(BlinkHalfPeriodMs * 2),
            RepeatBehavior = RepeatBehavior.Forever,
        };

        // Discrete frames, so the row snaps fully on/off like a real LED strip instead of fading.
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(BlinkHalfPeriodMs))));
        animation.Freeze();
        return animation;
    }
}
