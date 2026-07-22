using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class FlagWidget : OverlayWindowBase
{
    private readonly FlagPanel _panel;
    private IReadOnlyList<FlagState> _lastFlags = [];

    public FlagWidget() : base("Flag", defaultLeft: 100, defaultTop: 420)
    {
        InitializeComponent();
        DataContext = this;
        _panel = (FlagPanel)Scaler.ScalableContent!;

        // DragMove (in OverlayWindowBase) only works where something is actually rendered to
        // hit-test against — with no flags active most of the time, the window could end up with
        // nothing visible to click, which is exactly what made it impossible to grab and move.
        // Re-render whenever edit mode flips so the placeholder appears/disappears immediately.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsEditMode))
            {
                Render();
            }
        };
    }

    public void UpdateState(IReadOnlyList<FlagState> flags)
    {
        _lastFlags = flags;
        Render();
    }

    /// <summary>While editing, the empty "None" placeholder always renders first (top), with any
    /// real active flags stacking below it — a stable, predictable spot to click-drag from
    /// regardless of how many flags happen to be active. Locked (not editing) with no flags active
    /// renders nothing at all, rather than leaving a placeholder box cluttering the screen when
    /// nothing's actually happening.</summary>
    private void Render()
    {
        if (!IsEditMode)
        {
            _panel.UpdateState(_lastFlags);
            return;
        }

        var withPlaceholder = new List<FlagState> { FlagState.None };
        withPlaceholder.AddRange(_lastFlags);
        _panel.UpdateState(withPlaceholder);
    }
}
