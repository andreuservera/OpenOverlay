using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

/// <summary>The cockpit dashboard, laid out as <see cref="Options"/> says. Same API for the floating
/// widget, the dashboard and the control-panel preview, so all three always show the same modules.</summary>
public partial class CockpitPanel : UserControl
{
    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
        nameof(Options), typeof(CockpitOptions), typeof(CockpitPanel),
        new PropertyMetadata(new CockpitOptions(), (d, e) => ((CockpitPanel)d).OnOptionsChanged(e)));

    public CockpitPanel()
    {
        InitializeComponent();
        PropertyChangedEventManager.AddHandler(Options, OnOptionChanged, string.Empty);
        ApplyOptions();
    }

    public CockpitOptions Options
    {
        get => (CockpitOptions)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    public void UpdateState(CockpitState state) => Dashboard.Update(state);

    private void ApplyOptions() =>
        Dashboard.SetLayout(Options.VisibleModules(), Options.ShowShiftLights, Options.ShowProximityRadar);

    private void OnOptionsChanged(DependencyPropertyChangedEventArgs e)
    {
        // Weak, so a thrown-away preview panel isn't kept alive by the long-lived shared options.
        if (e.OldValue is CockpitOptions old)
        {
            PropertyChangedEventManager.RemoveHandler(old, OnOptionChanged, string.Empty);
        }

        if (e.NewValue is CockpitOptions current)
        {
            PropertyChangedEventManager.AddHandler(current, OnOptionChanged, string.Empty);
        }

        ApplyOptions();
    }

    private void OnOptionChanged(object? sender, PropertyChangedEventArgs e) => ApplyOptions();
}
