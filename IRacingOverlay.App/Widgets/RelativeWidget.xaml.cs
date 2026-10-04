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

    public void SetSessionType(string label) => _panel.SetSessionType(label);

    public void SetSof(double sof) => _panel.SetSof(sof);

    public void SetConditions(TableConditions conditions) => _panel.SetConditions(conditions);

    public void SetProgress(SessionProgress progress) => _panel.SetProgress(progress);

    public void SetOptions(DriverTableOptions options) => _panel.Options = options;
}
