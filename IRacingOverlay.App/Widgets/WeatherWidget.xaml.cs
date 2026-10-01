using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class WeatherWidget : OverlayWindowBase
{
    private readonly WeatherPanel _panel;

    public WeatherWidget() : base("Weather", defaultLeft: 1500, defaultTop: 20)
    {
        InitializeComponent();
        DataContext = this;
        _panel = (WeatherPanel)Scaler.ScalableContent!;
    }

    public void SetOptions(WeatherOptions options) => _panel.Options = options;

    public void SetCompassInterval(int intervalMs) => _panel.CompassIntervalMs = intervalMs;

    public void UpdateState(WeatherState state) => _panel.UpdateState(state);
}
