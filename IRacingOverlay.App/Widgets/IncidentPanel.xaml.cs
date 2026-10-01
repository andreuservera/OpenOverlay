using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class IncidentPanel : UserControl
{
    private static readonly TimeSpan FadeIn = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan Hold = TimeSpan.FromSeconds(3.5);
    private static readonly TimeSpan FadeOut = TimeSpan.FromMilliseconds(450);

    private readonly IncidentAlertTracker _alerts = new();

    public IncidentPanel()
    {
        InitializeComponent();
    }

    public void UpdateState(IncidentState state)
    {
        // Empty = disconnected or cleared: the next real count is a baseline, not an incident.
        if (ReferenceEquals(state, IncidentState.Empty))
        {
            _alerts.Reset();
            StopAlert();
        }
        else if (_alerts.Observe(state.MyIncidentCount, TimeSpan.FromMilliseconds(Environment.TickCount64), state.LatestReport) is { } alert)
        {
            if (alert.IsCorrection)
            {
                AlertText.Text = alert.Display;
            }
            else
            {
                ShowAlert(alert);
            }
        }

        var isTeamRace = state.TeamIncidentCount is not null;
        var severity = state.Severity switch
        {
            IncidentSeverity.Critical => StatePalette.Critical,
            IncidentSeverity.Warning => StatePalette.Warning,
            _ => StatePalette.TextPrimary,
        };

        MyCountText.Text = state.MyIncidentCount.ToString(CultureInfo.InvariantCulture);
        MyCaption.Visibility = isTeamRace ? Visibility.Visible : Visibility.Collapsed;
        TeamBlock.Visibility = isTeamRace ? Visibility.Visible : Visibility.Collapsed;
        TeamCountText.Text = state.TeamIncidentCount?.ToString(CultureInfo.InvariantCulture) ?? "";

        // The limit applies to the team total in a team race, so that's the number that changes colour.
        MyCountText.Foreground = isTeamRace ? StatePalette.TextPrimary : severity;
        TeamCountText.Foreground = severity;
        LimitText.Text = state.Limit is { } limit ? $"/ {limit.ToString(CultureInfo.InvariantCulture)}" : "";
    }

    /// <summary>Title crossfades to the incident, the panel washes in its colour and the count
    /// pulses; everything eases back on its own. A second incident restarts it from the current values.</summary>
    internal void ShowAlert(IncidentAlert alert)
    {
        var brush = alert.Points switch
        {
            0 => StatePalette.TextMuted,
            1 => StatePalette.Accent,
            2 => StatePalette.Warning,
            _ => StatePalette.Critical,
        };

        AlertText.Text = alert.Display;
        AlertText.Foreground = brush;
        AlertWash.Background = Wash(((SolidColorBrush)brush).Color);

        var total = FadeIn + Hold + FadeOut;
        AlertText.BeginAnimation(OpacityProperty, Envelope(0, 1, total));
        TitleText.BeginAnimation(OpacityProperty, Envelope(1, 0, total));
        AlertWash.BeginAnimation(OpacityProperty, Envelope(0, 1, FadeIn + TimeSpan.FromSeconds(1) + FadeOut * 2));

        // A light contact costs nothing: named, but the count it didn't change doesn't pulse.
        if (alert.Points == 0)
        {
            return;
        }

        var pulse = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(600) };
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1.22, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160)), new SineEase { EasingMode = EasingMode.EaseOut }));
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(600)), new SineEase { EasingMode = EasingMode.EaseInOut }));
        MyCountScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        MyCountScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
    }

    private void StopAlert()
    {
        AlertText.BeginAnimation(OpacityProperty, null);
        TitleText.BeginAnimation(OpacityProperty, null);
        AlertWash.BeginAnimation(OpacityProperty, null);
        MyCountScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        MyCountScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
    }

    /// <summary>Eases from <paramref name="rest"/> to <paramref name="peak"/>, holds, and eases back.</summary>
    private static DoubleAnimationUsingKeyFrames Envelope(double rest, double peak, TimeSpan total)
    {
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        var envelope = new DoubleAnimationUsingKeyFrames { Duration = total };
        envelope.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromTimeSpan(FadeIn), ease));
        envelope.KeyFrames.Add(new LinearDoubleKeyFrame(peak, KeyTime.FromTimeSpan(total - FadeOut)));
        envelope.KeyFrames.Add(new EasingDoubleKeyFrame(rest, KeyTime.FromTimeSpan(total), ease));
        return envelope;
    }

    private static Brush Wash(Color color)
    {
        var brush = new LinearGradientBrush(
            Color.FromArgb(0x59, color.R, color.G, color.B),
            Color.FromArgb(0x0F, color.R, color.G, color.B),
            0);
        brush.Freeze();
        return brush;
    }
}
