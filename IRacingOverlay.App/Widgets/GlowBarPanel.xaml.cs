using System.Windows.Controls;
using System.Windows.Media;

namespace IRacingOverlay.App.Widgets;

/// <summary>Generic vertical "glows/flashes when active" bar, reused for both ABS and TC.</summary>
public partial class GlowBarPanel : UserControl
{
    private Brush _idle = Brushes.Black;
    private Brush _dim = Brushes.Gray;
    private Brush _bright = Brushes.White;

    public GlowBarPanel()
    {
        InitializeComponent();
    }

    public void Configure(Color idle, Color dim, Color bright)
    {
        _idle = new SolidColorBrush(idle);
        _dim = new SolidColorBrush(dim);
        _bright = new SolidColorBrush(bright);
        Bar.Background = _idle;
    }

    /// <summary>blinkPhase is supplied by the caller (toggled once per tick) so every indicator in
    /// the cockpit display flashes in sync rather than drifting independently.</summary>
    public void SetActive(bool active, bool blinkPhase) =>
        Bar.Background = active ? (blinkPhase ? _bright : _dim) : _idle;
}
