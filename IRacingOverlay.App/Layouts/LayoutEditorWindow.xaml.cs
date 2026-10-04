using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using IRacingOverlay.App.ControlPanel;
using IRacingOverlay.App.Diagnostics;
using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.Layouts;

/// <summary>
/// The layout editor. All state and every change go through <see cref="LayoutEditorModel"/>; this
/// window draws it and turns mouse gestures into its operations. It edits one layout at a time on a
/// working copy, and only the Save button writes to the store.
/// </summary>
public partial class LayoutEditorWindow : Window
{
    private const string DragFormat = "OpenOverlay.WidgetType";

    /// <summary>How close, in screen pixels, an edge has to come to another before it snaps to it.</summary>
    private const double AlignThreshold = 6;

    /// <summary>How long after a bin click a double-click in the catalog is ignored, in ms.</summary>
    private const long CatalogRemoveGrace = 600;

    private readonly LayoutStore _store;
    private readonly MonitorCatalog _monitors;
    private readonly Func<string, LayoutWidget> _newWidget;
    private readonly ObservableCollection<EditorWidgetItem> _items = [];

    private LayoutEditorModel _model;
    private string? _selected;
    private double _scale = 1;

    /// <summary>The zoom chosen with the zoom bar, or null to fit the layout in the window.</summary>
    private double? _zoom;
    private bool _syncing;
    private string _catalogState = "";
    private string? _settingsFor;
    private string _settingsBasis = "";
    private bool _applyingSettings;

    private Point? _catalogPress;
    private long _catalogRemovedAt = long.MinValue / 2;
    private Gesture? _gesture;

    /// <param name="newWidget">Builds the entry for a widget type added to the layout: the
    /// individual widget's current size, opacity, auto-hide and config, as the spec asks.</param>
    internal LayoutEditorWindow(LayoutStore store, MonitorCatalog monitors, Func<string, LayoutWidget> newWidget, Layout layout)
    {
        InitializeComponent();
        _store = store;
        _monitors = monitors;
        _newWidget = newWidget;
        SizeBox.ItemsSource = ScaleLevels.Labels;
        Items.ItemsSource = _items;
        _model = Attach(new LayoutEditorModel(layout));
        Closing += OnClosing;
        Sync();
    }

    /// <summary>Raised after the layout has been saved, so whatever lists layouts can refresh.</summary>
    public event Action? Saved;

    public Guid LayoutId => _model.Layout.Id;

    public bool IsDirty => _model.IsDirty;

    /// <summary>Switches to another layout, asking first about unsaved changes to this one. False
    /// when the user chose to stay.</summary>
    public bool TryOpen(Layout layout)
    {
        if (layout.Id == LayoutId)
        {
            return true;
        }

        if (!ConfirmLeave())
        {
            return false;
        }

        _selected = null;
        _settingsFor = null;
        _items.Clear();
        _model = Attach(new LayoutEditorModel(layout));
        Sync();
        return true;
    }

    /// <summary>The layout was renamed from the Layouts page while open here.</summary>
    public void AdoptName(string name) => _model.AdoptName(name);

    /// <summary>The layout was deleted from the Layouts page: there is nothing left to save to.</summary>
    public void CloseDiscarding()
    {
        Closing -= OnClosing;
        Close();
    }

    private LayoutEditorModel Attach(LayoutEditorModel model)
    {
        model.Changed += Sync;
        SnapToggle.IsChecked = model.Layout.SnapEnabled;
        GridSizeBox.Text = model.Layout.GridSize.ToString(CultureInfo.InvariantCulture);
        return model;
    }

    // ===== Drawing the model =====

    /// <summary>Brings every part of the window in line with the model. Called after each change;
    /// cheap, since a layout has at most one widget of each type.</summary>
    private void Sync()
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        try
        {
            var layout = _model.Layout;
            SyncItems(layout);

            Surface.Width = layout.Width;
            Surface.Height = layout.Height;
            ApplyZoom();

            LayoutTitle.Text = layout.Name + (_model.IsDirty ? "  •  unsaved changes" : "");
            LayoutSubtitle.Text = string.Create(CultureInfo.InvariantCulture,
                $"{layout.Width}×{layout.Height} · {layout.Monitor.FriendlyName} · {layout.Widgets.Count} of {Layout.MaxWidgets} widgets");
            Title = $"{layout.Name} — Layout editor";
            UndoButton.IsEnabled = _model.CanUndo;
            RedoButton.IsEnabled = _model.CanRedo;
            SaveButton.IsEnabled = _model.IsDirty;
            SnapToggle.IsChecked = layout.SnapEnabled;
            if (!GridSizeBox.IsKeyboardFocusWithin)
            {
                GridSizeBox.Text = layout.GridSize.ToString(CultureInfo.InvariantCulture);
            }

            // Rebuilt only when what can be added changes, not on every mouse move of a drag.
            var catalogState = string.Join(',', layout.Widgets.Select(widget => widget.Type).Order());
            if (catalogState != _catalogState || Catalog.ItemsSource is null)
            {
                _catalogState = catalogState;
                Catalog.ItemsSource = WidgetCatalog.All.Select(descriptor => new CatalogEntry(descriptor, !layout.Contains(descriptor.Key))).ToList();
            }

            CatalogLimit.Text = layout.Widgets.Count >= Layout.MaxWidgets
                ? $"Every widget is in this layout ({Layout.MaxWidgets} of {Layout.MaxWidgets}). Each widget can be added once."
                : "";

            UpdateGridLines();
            SyncProperties();
        }
        finally
        {
            _syncing = false;
        }
    }

    private void SyncItems(Layout layout)
    {
        foreach (var gone in _items.Where(item => !layout.Contains(item.Type)).ToList())
        {
            _items.Remove(gone);
        }

        foreach (var widget in layout.Widgets)
        {
            if (_items.FirstOrDefault(item => item.Type == widget.Type) is { } existing)
            {
                existing.Bind(widget);
            }
            else
            {
                _items.Add(new EditorWidgetItem(widget));
            }
        }

        if (_selected is not null && !layout.Contains(_selected))
        {
            _selected = null;
        }

        foreach (var item in _items)
        {
            item.CanvasScale = _scale;
            item.IsSelected = item.Type == _selected;
            item.IsOffCanvas = IsOffCanvas(item);
        }
    }

    private void SyncProperties()
    {
        var item = SelectedItem;
        NoSelection.Visibility = item is null ? Visibility.Visible : Visibility.Collapsed;
        SelectionPanel.Visibility = item is null ? Visibility.Collapsed : Visibility.Visible;
        if (item is null)
        {
            SyncSettings(null);
            return;
        }

        var widget = item.Widget;
        SelectionName.Text = item.Descriptor.Name;
        if (!XBox.IsKeyboardFocusWithin)
        {
            XBox.Text = Math.Round(widget.X).ToString(CultureInfo.InvariantCulture);
        }

        if (!YBox.IsKeyboardFocusWithin)
        {
            YBox.Text = Math.Round(widget.Y).ToString(CultureInfo.InvariantCulture);
        }

        SizeBox.SelectedIndex = Array.IndexOf(Enum.GetValues<ScaleLevel>(), widget.Scale);
        MeasuredSize.Text = widget.Width > 0
            ? string.Create(CultureInfo.InvariantCulture, $"About {Math.Round(widget.Width)} × {Math.Round(widget.Height)} px with sample data. Tables grow and shrink with the field.")
            : "";
        XBox.IsEnabled = YBox.IsEnabled = SizeBox.IsEnabled = !widget.Locked;
        LockToggle.IsChecked = widget.Locked;
        HideToggle.IsChecked = !widget.Visible;
        SyncSettings(item);
    }

    // ===== The selected widget's settings =====

    /// <summary>
    /// Shows the selected widget's settings: opacity and auto-hide, then its own options built by
    /// <see cref="WidgetSettings"/> — the very rows its control panel page shows. Rebuilt when the
    /// selection changes or when undo/redo brings back different values, never because of a change
    /// made in these rows themselves (that would tear the slider out from under the pointer).
    /// </summary>
    private void SyncSettings(EditorWidgetItem? item)
    {
        if (item is null)
        {
            _settingsFor = null;
            WidgetSettingsList.ItemsSource = null;
            return;
        }

        var basis = SettingsBasis(item.Widget);
        if (_applyingSettings || (_settingsFor == item.Type && basis == _settingsBasis))
        {
            return;
        }

        _settingsFor = item.Type;
        _settingsBasis = basis;
        var type = item.Type;
        var widget = item.Widget;
        List<SettingsGroup> groups =
        [
            new SettingsGroup("PLACEMENT", "Size and position are set above.")
                .With(
                    new SliderSetting(
                        "Opacity",
                        null,
                        widget.Opacity,
                        0,
                        1,
                        0.01,
                        value => ApplySetting(() => _model.SetOpacity(type, value))),
                    new ToggleSetting(
                        "Hide when I'm not driving",
                        "Hidden in menus, the garage, replays and while spectating.",
                        widget.HideOutsideCar,
                        value => ApplySetting(() => _model.SetHideOutsideCar(type, value)))),
            .. WidgetSettings.For(type, item.SettingsContext(() => ApplySetting(() => _model.SetConfig(type, item.ReadConfig())))),
        ];

        // Names each change in the activity log after where it was made, as the control panel does.
        foreach (var group in groups)
        {
            var path = $"Layout editor › {item.Descriptor.Name} › {CultureInfo.InvariantCulture.TextInfo.ToTitleCase(group.Title.ToLowerInvariant())} › ";
            foreach (var setting in group.Items)
            {
                setting.TracePath = path;
                if (setting is ChipGroupSetting chips)
                {
                    foreach (var chip in chips.Chips)
                    {
                        chip.TracePath = $"{path}{chips.Label} › ";
                    }
                }
                else if (setting is ReorderListSetting columns)
                {
                    foreach (var chip in columns.Items.SelectMany(column => new[] { column.Visible, column.Companion }).OfType<ChipSetting>())
                    {
                        chip.TracePath = $"{path}{columns.Label} › ";
                    }
                }
            }
        }

        WidgetSettingsList.ItemsSource = groups;
    }

    /// <summary>Records a change made in the settings rows, which already show it.</summary>
    private void ApplySetting(Action change)
    {
        _applyingSettings = true;
        try
        {
            change();
        }
        finally
        {
            _applyingSettings = false;
        }

        if (SelectedItem is { } item)
        {
            _settingsBasis = SettingsBasis(item.Widget);
        }
    }

    private static string SettingsBasis(LayoutWidget widget) =>
        string.Create(CultureInfo.InvariantCulture, $"{widget.Config.ToJsonString()}|{widget.Opacity:R}|{widget.HideOutsideCar}");

    private EditorWidgetItem? SelectedItem => _items.FirstOrDefault(item => item.Type == _selected);

    private bool IsOffCanvas(EditorWidgetItem item) =>
        item.Widget.Width > 0 &&
        LayoutGeometry.IsOffCanvas(BoxOf(item.Widget), _model.Layout.Width, _model.Layout.Height);

    private static Box BoxOf(LayoutWidget widget) => new(widget.X, widget.Y, widget.Width, widget.Height);

    private void Select(string? type)
    {
        _selected = type;
        foreach (var item in _items)
        {
            item.IsSelected = item.Type == type;
        }

        SyncProperties();
    }

    // ===== Zoom and grid =====

    /// <summary>The zoom steps − and + move between, as fractions of the layout's real size.</summary>
    private static readonly double[] ZoomSteps = [0.1, 0.15, 0.2, 0.25, 0.33, 0.4, 0.5, 0.67, 0.75, 0.9, 1, 1.25, 1.5, 2, 3];

    private void ApplyZoom()
    {
        // Fitted, the canvas never needs scrolling; zoomed by hand, it may be larger than the view.
        var fitted = _zoom is null;
        Viewport.HorizontalScrollBarVisibility = fitted ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        Viewport.VerticalScrollBarVisibility = fitted ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;

        var scale = _zoom ?? FitScale();
        ZoomLabel.Content = string.Create(CultureInfo.InvariantCulture, $"{Math.Round(scale * 100)}%");
        ZoomOutButton.IsEnabled = scale > ZoomSteps[0] + 0.001;
        ZoomInButton.IsEnabled = scale < ZoomSteps[^1] - 0.001;
        ZoomFitButton.IsEnabled = !fitted;

        if (Math.Abs(scale - _scale) < 0.0001 && Math.Abs(CanvasScale.ScaleX - scale) < 0.0001)
        {
            return;
        }

        _scale = scale;
        CanvasScale.ScaleX = scale;
        CanvasScale.ScaleY = scale;
        Frame.BorderThickness = new Thickness(1 / scale);
        foreach (var item in _items)
        {
            item.CanvasScale = scale;
        }

        UpdateGridLines();
    }

    private void UpdateGridLines()
    {
        var layout = _model.Layout;
        if (!layout.SnapEnabled || layout.GridSize * _scale < 8)
        {
            // Lines closer than this on screen are a texture, not a guide: snapping still works.
            GridLines.Fill = null;
            return;
        }

        var size = layout.GridSize;
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), 1 / _scale);
        pen.Freeze();
        var lines = new GeometryGroup();
        lines.Children.Add(new LineGeometry(new Point(0, 0), new Point(size, 0)));
        lines.Children.Add(new LineGeometry(new Point(0, 0), new Point(0, size)));
        var brush = new DrawingBrush(new GeometryDrawing(null, pen, lines))
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, size, size),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, size, size),
            ViewboxUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        GridLines.Fill = brush;
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e) => ApplyZoom();

    /// <summary>The scale that shows the whole layout in the window, never above real size.</summary>
    private double FitScale()
    {
        if (Viewport.ActualWidth <= 0)
        {
            return _scale;
        }

        var margin = Frame.Margin.Left + Frame.Margin.Right;
        var scale = Math.Min(
            (Viewport.ActualWidth - margin) / _model.Layout.Width,
            (Viewport.ActualHeight - margin) / _model.Layout.Height);
        return Math.Clamp(scale, 0.05, 1);
    }

    private void OnZoomIn(object sender, RoutedEventArgs e) =>
        ZoomTo(ZoomSteps.FirstOrDefault(step => step > _scale + 0.001, ZoomSteps[^1]));

    private void OnZoomOut(object sender, RoutedEventArgs e) =>
        ZoomTo(ZoomSteps.LastOrDefault(step => step < _scale - 0.001, ZoomSteps[0]));

    private void OnZoomActualSize(object sender, RoutedEventArgs e) => ZoomTo(1);

    private void OnZoomFit(object sender, RoutedEventArgs e) => ZoomTo(null);

    /// <summary>Zooms keeping the point at the centre of the view where it is, so zooming in goes
    /// towards what was being looked at instead of the top-left corner.</summary>
    private void ZoomTo(double? zoom)
    {
        var viewCentre = new Point(Viewport.ViewportWidth / 2, Viewport.ViewportHeight / 2);
        var focus = Viewport.TranslatePoint(viewCentre, Surface);

        _zoom = zoom;
        ApplyZoom();
        if (zoom is null)
        {
            return;
        }

        Viewport.UpdateLayout();
        var now = Surface.TranslatePoint(focus, Viewport);
        Viewport.ScrollToHorizontalOffset(Viewport.HorizontalOffset + now.X - viewCentre.X);
        Viewport.ScrollToVerticalOffset(Viewport.VerticalOffset + now.Y - viewCentre.Y);
    }

    private void OnSnapToggled(object sender, RoutedEventArgs e) =>
        _model.SetSnap(SnapToggle.IsChecked == true, _model.Layout.GridSize);

    /// <summary>Applied while typing, so the grid redraws at once; leaving the field only tidies its
    /// text. Partial or invalid text (empty, "0") is left alone until it becomes a size.</summary>
    private void OnGridSizeTyped(object sender, TextChangedEventArgs e)
    {
        if (GridSizeBox.IsKeyboardFocusWithin &&
            int.TryParse(GridSizeBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var size) && size > 0)
        {
            _model.SetGridSize(size);
        }
    }

    private void OnGridSizeCommitted(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(GridSizeBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var size) && size > 0)
        {
            _model.SetSnap(_model.Layout.SnapEnabled, size);
        }

        GridSizeBox.Text = _model.Layout.GridSize.ToString(CultureInfo.InvariantCulture);
    }

    // ===== Adding from the catalog =====

    private void OnCatalogMouseDown(object sender, MouseButtonEventArgs e)
    {
        _catalogPress = e.GetPosition(Catalog);
        if (e.ClickCount != 2 || (e.OriginalSource as DependencyObject)?.FindAncestor<ButtonBase>() is not null)
        {
            return;
        }

        // The second click of a quick double-click on the bin lands on the row the removal just
        // freed; it must not put the widget straight back.
        if (Environment.TickCount64 - _catalogRemovedAt < CatalogRemoveGrace)
        {
            return;
        }

        _catalogPress = null;
        if ((e.OriginalSource as DependencyObject)?.FindAncestorDataContext<CatalogEntry>() is not { } entry)
        {
            return;
        }

        e.Handled = true;
        if (!entry.IsAvailable)
        {
            Status($"{entry.Descriptor.Name} is already in this layout. Each widget can be added once.");
            return;
        }

        AddAtCentre(entry.Descriptor.Key);
    }

    /// <summary>Adds a widget centred on the part of the canvas in view: what a double-click does.
    /// It is placed once its real size is known, so it lands centred and not hanging off its
    /// top-left corner.</summary>
    private void AddAtCentre(string type)
    {
        var centre = Viewport.TranslatePoint(new Point(Viewport.ViewportWidth / 2, Viewport.ViewportHeight / 2), Surface);
        var x = Math.Clamp(centre.X, 0, _model.Layout.Width - 1);
        var y = Math.Clamp(centre.Y, 0, _model.Layout.Height - 1);
        if (!TryAdd(type, x, y))
        {
            return;
        }

        Items.UpdateLayout();
        if (_model.Layout.WidgetOf(type) is { Width: > 0, Height: > 0 } widget)
        {
            // Part of the same step as the add: undo takes the widget away, redo brings it back here.
            _model.PreviewMove(type,
                Snap(Math.Clamp(Math.Round(x - widget.Width / 2), 0, Math.Max(0, _model.Layout.Width - widget.Width))),
                Snap(Math.Clamp(Math.Round(y - widget.Height / 2), 0, Math.Max(0, _model.Layout.Height - widget.Height))));
        }
    }

    private double Snap(double value) =>
        _model.Layout.SnapEnabled ? Math.Round(value / _model.Layout.GridSize) * _model.Layout.GridSize : value;

    private void OnCatalogRemove(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CatalogEntry entry)
        {
            return;
        }

        _catalogRemovedAt = Environment.TickCount64;
        _model.Remove(entry.Descriptor.Key);
        Status($"Removed {entry.Descriptor.Name}. Undo brings it back.");
    }

    private void OnCatalogMouseMove(object sender, MouseEventArgs e)
    {
        if (_catalogPress is not { } press || e.LeftButton != MouseButtonState.Pressed)
        {
            _catalogPress = null;
            return;
        }

        var now = e.GetPosition(Catalog);
        if (Math.Abs(now.X - press.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(now.Y - press.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _catalogPress = null;
        if ((e.OriginalSource as DependencyObject)?.FindAncestorDataContext<CatalogEntry>() is not { } entry)
        {
            return;
        }

        if (!entry.IsAvailable)
        {
            Status($"{entry.Descriptor.Name} is already in this layout. Each widget can be added once.");
            return;
        }

        DragGhostIcon.Data = Geometry.Parse(entry.Descriptor.IconData);
        DragGhostName.Text = entry.Descriptor.Name;
        DragGhostScale.ScaleX = DragGhostScale.ScaleY = 1 / _scale;
        DropZoneText.Text = $"Drop {entry.Descriptor.Name} anywhere on the screen";
        DropZone.Visibility = Visibility.Visible;
        try
        {
            DragDrop.DoDragDrop(Catalog, new DataObject(DragFormat, entry.Descriptor.Key), DragDropEffects.Copy);
        }
        finally
        {
            DropZone.Visibility = Visibility.Collapsed;
            DragGhost.Visibility = Visibility.Collapsed;
        }
    }

    private void OnSurfaceDragOver(object sender, DragEventArgs e)
    {
        var accepted = e.Data.GetData(DragFormat) is string type && !_model.Layout.Contains(type);
        e.Effects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;

        DragGhost.Visibility = accepted ? Visibility.Visible : Visibility.Collapsed;
        var point = e.GetPosition(Surface);
        Canvas.SetLeft(DragGhost, Snap(Math.Clamp(point.X, 0, _model.Layout.Width - 1)));
        Canvas.SetTop(DragGhost, Snap(Math.Clamp(point.Y, 0, _model.Layout.Height - 1)));
    }

    private void OnSurfaceDragLeave(object sender, DragEventArgs e) => DragGhost.Visibility = Visibility.Collapsed;

    private void OnSurfaceDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DragFormat) is not string type)
        {
            return;
        }

        DragGhost.Visibility = Visibility.Collapsed;
        var point = e.GetPosition(Surface);
        TryAdd(type, point.X, point.Y);
    }

    /// <summary>Adds a widget with its top-left corner at a canvas point (snapped if snap is on).</summary>
    private bool TryAdd(string type, double x, double y)
    {
        var widget = _newWidget(type);
        widget.X = Snap(Math.Round(Math.Clamp(x, 0, _model.Layout.Width - 1)));
        widget.Y = Snap(Math.Round(Math.Clamp(y, 0, _model.Layout.Height - 1)));

        try
        {
            _model.Add(widget);
            Select(type);
            Status($"Added {NameOf(type)}.");
            return true;
        }
        catch (LayoutRuleException rule)
        {
            Status(rule.Message);
            return false;
        }
    }

    // ===== Moving and resizing =====

    /// <summary>A drag in progress: which widget, where the pointer and the widget started, and for
    /// a resize, which corner and the widget's size at level 1.0.</summary>
    private sealed record Gesture(string Type, Point Start, Box Origin, string? Corner, double NaturalWidth, double NaturalHeight);

    /// <summary>A click that reached the canvas missed every widget (theirs are handled): deselect.</summary>
    private void OnSurfaceMouseDown(object sender, MouseButtonEventArgs e) => Select(null);

    /// <summary>Any click on the canvas takes the focus off the property fields: what was typed in
    /// X or Y is applied, and the fields follow the widget again as it is dragged (a focused field
    /// is never overwritten, so it would otherwise keep its old value until clicked away).</summary>
    private void OnSurfacePreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!Surface.IsKeyboardFocused)
        {
            Surface.Focus();
        }
    }

    /// <summary>
    /// Arrow keys move the selected widget: one layout pixel per press, or one grid step with Shift.
    /// Only while the canvas has the keyboard (it takes it on any click), so typing in the
    /// position or grid boxes is never hijacked.
    /// </summary>
    private void OnSurfaceKeyDown(object sender, KeyEventArgs e)
    {
        var (dx, dy) = e.Key switch
        {
            Key.Left => (-1, 0),
            Key.Right => (1, 0),
            Key.Up => (0, -1),
            Key.Down => (0, 1),
            _ => (0, 0),
        };
        if ((dx, dy) == (0, 0) || SelectedItem is not { } item || _gesture is not null)
        {
            return;
        }

        // Handled either way, so an arrow never moves the keyboard focus off the canvas instead.
        e.Handled = true;
        if (item.IsLocked)
        {
            Status($"{item.Descriptor.Name} is locked. Unlock it to move it.");
            return;
        }

        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? _model.Layout.GridSize : 1;
        _model.Nudge(item.Type, dx * step, dy * step);
    }

    private void OnItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not EditorWidgetItem item)
        {
            return;
        }

        Select(item.Type);
        e.Handled = true;
        if (item.IsLocked)
        {
            Status($"{item.Descriptor.Name} is locked. Unlock it to move it.");
            return;
        }

        _gesture = new Gesture(item.Type, e.GetPosition(Surface), BoxOf(item.Widget), null, 0, 0);
        _model.BeginGesture();
        ((UIElement)sender).CaptureMouse();
    }

    private void OnItemMouseMove(object sender, MouseEventArgs e)
    {
        if (_gesture is not { Corner: null } gesture || !((UIElement)sender).IsMouseCaptured)
        {
            return;
        }

        var pointer = e.GetPosition(Surface);
        var moved = gesture.Origin with
        {
            X = gesture.Origin.X + pointer.X - gesture.Start.X,
            Y = gesture.Origin.Y + pointer.Y - gesture.Start.Y,
        };
        var layout = _model.Layout;
        var others = layout.Widgets
            .Where(widget => widget.Type != gesture.Type && widget.Visible && widget.Width > 0)
            .Select(BoxOf);
        var snap = LayoutGeometry.Snap(moved, others, layout.Width, layout.Height, AlignThreshold / _scale,
            layout.SnapEnabled ? layout.GridSize : null);

        _model.PreviewMove(gesture.Type, Math.Round(snap.X), Math.Round(snap.Y));
        DrawGuides(snap.Guides);
    }

    private void OnItemMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_gesture is not { Corner: null })
        {
            return;
        }

        ((UIElement)sender).ReleaseMouseCapture();
        EndGesture();
    }

    private void OnHandleMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { DataContext: EditorWidgetItem item, Tag: string corner } handle || item.IsLocked)
        {
            return;
        }

        var widget = item.Widget;
        if (widget.Width <= 0 || widget.Height <= 0)
        {
            return;
        }

        var factor = ScaleLevels.FactorOf(widget.Scale);
        _gesture = new Gesture(item.Type, e.GetPosition(Surface), BoxOf(widget), corner, widget.Width / factor, widget.Height / factor);
        _model.BeginGesture();
        handle.CaptureMouse();
    }

    /// <summary>Resizing steps through the size levels: the pointer proposes a size, the nearest
    /// level wins, and the corner opposite the handle stays where it was.</summary>
    private void OnHandleMouseMove(object sender, MouseEventArgs e)
    {
        if (_gesture is not { Corner: { } corner } gesture || !((UIElement)sender).IsMouseCaptured)
        {
            return;
        }

        var pointer = e.GetPosition(Surface);
        var origin = gesture.Origin;
        var left = corner.EndsWith("Left", StringComparison.Ordinal);
        var top = corner.StartsWith("Top", StringComparison.Ordinal);
        var anchorX = left ? origin.Right : origin.X;
        var anchorY = top ? origin.Bottom : origin.Y;
        var wantedWidth = Math.Max(1, left ? anchorX - pointer.X : pointer.X - anchorX);
        var wantedHeight = Math.Max(1, top ? anchorY - pointer.Y : pointer.Y - anchorY);

        var level = LayoutGeometry.NearestLevel(Math.Max(wantedWidth / gesture.NaturalWidth, wantedHeight / gesture.NaturalHeight));
        var factor = ScaleLevels.FactorOf(level);
        var width = gesture.NaturalWidth * factor;
        var height = gesture.NaturalHeight * factor;
        _model.PreviewResize(
            gesture.Type,
            level,
            Math.Round(left ? anchorX - width : anchorX),
            Math.Round(top ? anchorY - height : anchorY));
        Status($"{NameOf(gesture.Type)}: size {ScaleLevels.LabelOf(level)}");
    }

    private void OnHandleMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_gesture is not { Corner: not null })
        {
            return;
        }

        e.Handled = true;
        ((UIElement)sender).ReleaseMouseCapture();
        EndGesture();
    }

    private void EndGesture()
    {
        _gesture = null;
        GuideLayer.Children.Clear();
        _model.EndGesture();
    }

    private void DrawGuides(IReadOnlyList<Guide> guides)
    {
        GuideLayer.Children.Clear();
        foreach (var guide in guides)
        {
            GuideLayer.Children.Add(new Line
            {
                X1 = guide.IsVertical ? guide.Position : 0,
                X2 = guide.IsVertical ? guide.Position : _model.Layout.Width,
                Y1 = guide.IsVertical ? 0 : guide.Position,
                Y2 = guide.IsVertical ? _model.Layout.Height : guide.Position,
                Stroke = (Brush)FindResource("Cp.Accent"),
                StrokeThickness = 1 / _scale,
                StrokeDashArray = [4, 3],
            });
        }
    }

    /// <summary>The preview re-measured a widget: keep its measured size and off-screen mark current.</summary>
    private void OnItemSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not EditorWidgetItem item)
        {
            return;
        }

        _model.SetMeasuredSize(item.Type, e.NewSize.Width, e.NewSize.Height);
        item.IsOffCanvas = IsOffCanvas(item);
        if (item.Type == _selected)
        {
            SyncProperties();
        }
    }

    // ===== Properties and commands =====

    private void OnItemRightDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is EditorWidgetItem item)
        {
            Select(item.Type);
        }
    }

    /// <summary>The widget a command applies to: the one whose context menu was used, else the
    /// selected one.</summary>
    private string? TargetOf(object sender) =>
        (sender as FrameworkElement)?.DataContext is EditorWidgetItem item ? item.Type : _selected;

    private void OnBringToFront(object sender, RoutedEventArgs e)
    {
        if (TargetOf(sender) is { } type)
        {
            _model.BringToFront(type);
        }
    }

    private void OnSendToBack(object sender, RoutedEventArgs e)
    {
        if (TargetOf(sender) is { } type)
        {
            _model.SendToBack(type);
        }
    }

    private void OnToggleLock(object sender, RoutedEventArgs e)
    {
        if (TargetOf(sender) is { } type && _model.Layout.WidgetOf(type) is { } widget)
        {
            _model.SetLocked(type, !widget.Locked);
        }
    }

    private void OnToggleHidden(object sender, RoutedEventArgs e)
    {
        if (TargetOf(sender) is { } type && _model.Layout.WidgetOf(type) is { } widget)
        {
            _model.SetVisible(type, !widget.Visible);
        }
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (TargetOf(sender) is { } type)
        {
            _model.Remove(type);
            Status($"Removed {NameOf(type)}.");
        }
    }

    private void OnPositionCommitted(object sender, RoutedEventArgs e)
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        if (double.TryParse(XBox.Text.Trim(), NumberStyles.Integer | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var x) &&
            double.TryParse(YBox.Text.Trim(), NumberStyles.Integer | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var y))
        {
            _model.Move(item.Type, Math.Round(x), Math.Round(y));
        }

        SyncProperties();
    }

    private void OnSizeChosen(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || SelectedItem is not { } item || SizeBox.SelectedIndex < 0)
        {
            return;
        }

        var level = Enum.GetValues<ScaleLevel>()[SizeBox.SelectedIndex];
        if (level != item.Widget.Scale)
        {
            // From the properties pane the top-left corner stays put.
            _model.Resize(item.Type, level, item.Widget.X, item.Widget.Y);
        }
    }

    private void OnUndo(object sender, RoutedEventArgs e) => _model.Undo();

    private void OnRedo(object sender, RoutedEventArgs e) => _model.Redo();

    private void OnChangeTarget(object sender, RoutedEventArgs e)
    {
        var layout = _model.Layout;
        if (LayoutTargetDialog.ForExisting(this, _monitors.Monitors(), layout) is not { } target)
        {
            return;
        }

        var scale = false;
        if (target.Width != layout.Width || target.Height != layout.Height)
        {
            var answer = LayoutDialog.Ask(
                this,
                "Change the resolution",
                string.Create(CultureInfo.InvariantCulture,
                    $"From {layout.Width}×{layout.Height} to {target.Width}×{target.Height}. Scale the widgets' positions to keep their place on screen, or keep their pixel positions?"),
                ["Scale positions", "Keep pixels", "Cancel"]);
            if (answer is < 0 or 2)
            {
                return;
            }

            scale = answer == 0;
        }

        _model.ChangeTarget(target.Monitor, target.Width, target.Height, scale);
    }

    private void OnSave(object sender, RoutedEventArgs e) => Save();

    private bool Save()
    {
        try
        {
            var saved = _store.Save(_model.Layout);
            _model.MarkSaved(saved);
            AppLog.Activity("Layouts", $"Saved layout \"{saved.Name}\"");
            Status($"Saved \"{saved.Name}\".");
            Saved?.Invoke();
            return true;
        }
        catch (Exception error) when (!ExceptionPolicy.IsFatal(error))
        {
            AppLog.Error("Layouts", "Could not save the layout", error);
            LayoutDialog.Inform(this, "Could not save the layout", error.Message);
            return false;
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!ConfirmLeave())
        {
            e.Cancel = true;
        }
    }

    /// <summary>Asks what to do with unsaved changes before leaving this layout. True to go ahead.</summary>
    private bool ConfirmLeave()
    {
        if (!_model.IsDirty)
        {
            return true;
        }

        var answer = LayoutDialog.Ask(
            this,
            "Unsaved changes",
            $"\"{_model.Layout.Name}\" has changes that haven't been saved.",
            ["Save", "Discard", "Cancel"]);
        return answer switch
        {
            0 => Save(),
            1 => true,
            _ => false,
        };
    }

    private void Status(string text) => StatusText.Text = text;

    private static string NameOf(string type) =>
        WidgetCatalog.All.FirstOrDefault(descriptor => descriptor.Key == type)?.Name ?? type;
}

internal static class VisualTreeExtensions
{
    /// <summary>The nearest data context of type <typeparamref name="T"/> at or above an element.</summary>
    public static T? FindAncestorDataContext<T>(this DependencyObject? node) where T : class
    {
        while (node is not null)
        {
            if (node is FrameworkElement { DataContext: T found })
            {
                return found;
            }

            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }

        return null;
    }

    /// <summary>The nearest element of type <typeparamref name="T"/> at or above an element.</summary>
    public static T? FindAncestor<T>(this DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T found)
            {
                return found;
            }

            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }

        return null;
    }
}
