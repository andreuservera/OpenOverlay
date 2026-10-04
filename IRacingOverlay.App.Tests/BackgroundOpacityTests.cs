using System.Threading;
using System.Windows.Controls;
using System.Windows.Media;
using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.Tests;

public class BackgroundOpacityTests
{
    [Fact]
    public void Value_FadesTheInheritedBackgroundBrushes_AndOneRestoresThem()
    {
        RunSta(() =>
        {
            var background = new SolidColorBrush(Color.FromArgb(0xF0, 0x10, 0x12, 0x14));
            var border = new SolidColorBrush(Colors.White) { Opacity = 0.5 };
            var host = new Border();
            host.Resources["Theme.PanelBackground"] = background;
            host.Resources["Theme.PanelBorder"] = border;
            var widget = new Border();
            host.Child = widget;

            BackgroundOpacity.SetValue(widget, 0.4);
            Assert.Equal(0.4, ((Brush)widget.Resources["Theme.PanelBackground"]).Opacity, 6);
            Assert.Equal(0.2, ((Brush)widget.Resources["Theme.PanelBorder"]).Opacity, 6);
            Assert.Equal(background.Color, ((SolidColorBrush)widget.Resources["Theme.PanelBackground"]).Color);

            // A second change fades the inherited brush again, not the earlier faded copy.
            BackgroundOpacity.SetValue(widget, 0.8);
            Assert.Equal(0.8, ((Brush)widget.Resources["Theme.PanelBackground"]).Opacity, 6);

            BackgroundOpacity.SetValue(widget, 1.0);
            Assert.False(widget.Resources.Contains("Theme.PanelBackground"));
            Assert.False(widget.Resources.Contains("Theme.PanelBorder"));
        });
    }

    [Fact]
    public void Value_ReachesAPanelThatDrawsItsBackgroundThroughADynamicResourceStyle()
    {
        RunSta(() =>
        {
            // Same shape as a widget: app-level brushes and style, a window, and the panel inside it.
            var app = new Border();
            app.Resources["Theme.PanelBackground"] = new SolidColorBrush(Colors.Black);
            var style = new System.Windows.Style(typeof(Border));
            style.Setters.Add(new System.Windows.Setter(Border.BackgroundProperty, new System.Windows.DynamicResourceExtension("Theme.PanelBackground")));
            var window = new Border();
            var panel = new Border { Style = style, Child = new TextBlock { Text = "P1" } };
            window.Child = panel;
            app.Child = window;
            Assert.Equal(1.0, panel.Background.Opacity, 6);

            BackgroundOpacity.SetValue(window, 0.3);
            Assert.Equal(0.3, panel.Background.Opacity, 6);
            Assert.Equal(1.0, panel.Opacity, 6);
            Assert.Equal(1.0, ((TextBlock)panel.Child).Opacity, 6);

            BackgroundOpacity.SetValue(window, 1.0);
            Assert.Equal(1.0, panel.Background.Opacity, 6);
        });
    }

    private static void RunSta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                test();
            }
            catch (Exception e)
            {
                failure = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }
}
