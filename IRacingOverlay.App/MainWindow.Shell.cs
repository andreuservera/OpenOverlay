using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using IRacingOverlay.App.About;
using IRacingOverlay.App.ControlPanel;
using IRacingOverlay.App.Dashboard;
using IRacingOverlay.App.Diagnostics;
using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.TrackMaps;
using IRacingOverlay.App.ViewModels;
using IRacingOverlay.App.Widgets;
using IRacingOverlay.Sdk;
using Screen = System.Windows.Forms.Screen;

namespace IRacingOverlay.App;

/// <summary>The control panel window itself: the setup backdrop, keyboard, the two panes and the rail.</summary>
public partial class MainWindow
{
    /// <summary>A click beside the assistant's card closes it, like Escape and its ✕.</summary>
    private void SetupBackdrop_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender))
        {
            _vm.CancelSetup();
        }
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape && _vm.IsSetupOpen)
        {
            _vm.CancelSetup();
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    /// <summary>Splits the room right of the rail between the settings and the preview as shares
    /// rather than pixels, so the split holds as the window is resized.</summary>
    private void ApplyPaneSplit(double configShare)
    {
        ConfigColumn.Width = new GridLength(configShare, GridUnitType.Star);
        PreviewColumn.Width = new GridLength(1 - configShare, GridUnitType.Star);
    }

    /// <summary>Remembers where the splitter was left, for this page and the next run.</summary>
    private void SavePaneSplit()
    {
        var total = ConfigColumn.ActualWidth + PreviewColumn.ActualWidth;
        if (total <= 0)
        {
            return;
        }

        var share = ConfigColumn.ActualWidth / total;
        PaneSplitStore.Save(share);
        ApplyPaneSplit(PaneSplitStore.ConfigShare);
    }

    /// <summary>
    /// A page with nothing to preview (the settings pages) gives the preview pane's room back: the
    /// pane and its splitter go, and the settings take the width, centred at a reading width.
    /// The split as the user left it comes back with the next page that has a preview.
    /// </summary>
    private void FitPanesToPage()
    {
        if (_vm.ShowsPreviewPlaceholder)
        {
            if (_paneWidths is not null)
            {
                return;
            }

            _paneWidths = (ConfigColumn.Width, PreviewColumn.Width);
            PreviewPane.Visibility = Visibility.Collapsed;
            PaneSplitter.Visibility = Visibility.Collapsed;
            PreviewColumn.MinWidth = 0;
            PreviewColumn.Width = new GridLength(0);
            ConfigColumn.Width = new GridLength(1, GridUnitType.Star);
            ConfigPane.MaxWidth = 840;
        }
        else if (_paneWidths is { } widths)
        {
            _paneWidths = null;
            ConfigPane.ClearValue(MaxWidthProperty);
            ConfigColumn.Width = widths.Config;
            PreviewColumn.MinWidth = 340;
            PreviewColumn.Width = widths.Preview;
            PaneSplitter.Visibility = Visibility.Visible;
            PreviewPane.Visibility = Visibility.Visible;
        }
    }

    /// <summary>A click in the rail ends a search, even on the page already open (which raises no
    /// selection change of its own).</summary>
    private void Rail_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_vm.IsSearching)
        {
            Dispatcher.BeginInvoke(_vm.ClearSearch);
        }
    }

    /// <summary>Search from the folded rail: unfolds it, so there is a box to type in.</summary>
    private void RailSearch_Click(object sender, RoutedEventArgs e)
    {
        _vm.IsRailCollapsed = false;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () => SearchBox.Focus());
    }

    /// <summary>A page opened from elsewhere (a link, a search result) may be below the rail's fold.</summary>
    private void Rail_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox { SelectedItem: { } item } list)
        {
            list.ScrollIntoView(item);
        }
    }

    /// <summary>Scrolls the setting a search result led to into view, with room above it so its
    /// section's heading still shows.</summary>
    private void RevealSetting(SettingItem item)
    {
        if (FindByDataContext(SettingsScroller, item) is { } row)
        {
            row.BringIntoView(new Rect(0, -60, row.ActualWidth, row.ActualHeight + 120));
        }
    }

    private static FrameworkElement? FindByDataContext(DependencyObject parent, object context)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement { } element && ReferenceEquals(element.DataContext, context))
            {
                return element;
            }

            if (FindByDataContext(child, context) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
