using System.Text.Json.Nodes;
using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.Layouts;

/// <summary>How an individual widget was set up before a layout took it over, so closing the
/// layout can put it back exactly. Left/Top are null when the widget had never been placed.</summary>
public sealed record WidgetSnapshot(
    string Type,
    bool Enabled,
    double? Left,
    double? Top,
    ScaleLevel Scale,
    double Opacity,
    bool HideOutsideCar,
    JsonObject Config);

/// <summary>The layout currently open and the snapshot to restore when it closes. Persisted so the
/// restore still works after the app is restarted with the layout open.</summary>
/// <param name="ScalePositions">Whether it was opened with its positions scaled to its monitor's
/// resolution, so saving it from the editor while open re-applies it the same way.</param>
public sealed record OpenLayoutState(Guid LayoutId, IReadOnlyList<WidgetSnapshot> Snapshot, bool ScalePositions = false);
