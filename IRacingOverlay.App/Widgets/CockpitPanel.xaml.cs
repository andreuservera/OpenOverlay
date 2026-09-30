using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using IRacingOverlay.App.ViewModels;
using IRacingOverlay.App.Widgets.Cockpit;

namespace IRacingOverlay.App.Widgets;

/// <summary>The cockpit, in whichever theme <see cref="Options"/> selects. Same API for the floating
/// widget, the dashboard and the control-panel preview, so all three always show the same theme.</summary>
public partial class CockpitPanel : UserControl
{
    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
        nameof(Options), typeof(CockpitOptions), typeof(CockpitPanel),
        new PropertyMetadata(new CockpitOptions()));

    public static readonly DependencyProperty ThemeProperty = DependencyProperty.Register(
        nameof(Theme), typeof(CockpitTheme), typeof(CockpitPanel),
        new PropertyMetadata(CockpitTheme.Default, (d, _) => ((CockpitPanel)d).ShowTheme()));

    private CockpitDashboard _dashboard = null!;
    private CockpitState _last = CockpitState.Empty;

    public CockpitPanel()
    {
        InitializeComponent();
        ShowTheme();
        // Bound rather than subscribed: the binding listens weakly, so a preview panel that is
        // thrown away isn't kept alive by the long-lived shared options object.
        SetBinding(ThemeProperty, new Binding($"{nameof(Options)}.{nameof(CockpitOptions.Theme)}") { Source = this });
    }

    public CockpitOptions Options
    {
        get => (CockpitOptions)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    public CockpitTheme Theme
    {
        get => (CockpitTheme)GetValue(ThemeProperty);
        set => SetValue(ThemeProperty, value);
    }

    public void UpdateState(CockpitState state)
    {
        _last = state;
        _dashboard.Update(state);
    }

    private void ShowTheme()
    {
        _dashboard = Theme switch
        {
            CockpitTheme.GtSports => new GtSportsCockpit(),
            CockpitTheme.Casual => new CasualCockpit(),
            CockpitTheme.Hypercar => new HypercarCockpit(),
            CockpitTheme.PitWall => new PitWallCockpit(),
            CockpitTheme.ClassicCar => new ClassicCarCockpit(),
            CockpitTheme.Invisible => new InvisibleCockpit(),
            _ => new DefaultCockpit(),
        };

        _dashboard.Update(_last);
        Host.Child = _dashboard;
    }
}
