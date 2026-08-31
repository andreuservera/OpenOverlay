using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class RelativeWidget : OverlayWindowBase
{
    private readonly RelativePanel _panel;

    public RelativeWidget() : base("Relative", defaultLeft: 100, defaultTop: 100)
    {
        InitializeComponent();
        DataContext = this;
        _panel = (RelativePanel)Scaler.ScalableContent!;
    }

    public void UpdateRows(IReadOnlyList<object> rows) => _panel.SetRows(rows);

    public void SetCarName(string carName) => _panel.SetCarName(carName);

    public void SetSessionId(int subSessionId) => _panel.SetSessionId(subSessionId);

    public void SetOptions(DriverTableOptions options) => _panel.Options = options;
}
