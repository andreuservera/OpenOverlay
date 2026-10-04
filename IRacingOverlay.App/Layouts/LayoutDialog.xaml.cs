using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IRacingOverlay.App.Layouts;

/// <summary>A themed question or name prompt for the layout screens.</summary>
public partial class LayoutDialog : Window
{
    private int _answer = -1;

    private LayoutDialog(Window? owner, string heading, string message, IReadOnlyList<string> answers, int primary)
    {
        InitializeComponent();
        Owner = owner is { IsVisible: true } ? owner : null;
        if (Owner is null)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        Heading.Text = heading;
        Message.Text = message;
        Message.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;

        for (var i = 0; i < answers.Count; i++)
        {
            var index = i;
            var button = new Button
            {
                Content = answers[i],
                MinWidth = 84,
                Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                Style = (Style)FindResource(i == primary ? "Cp.PrimaryButton" : "Cp.Button"),
            };
            button.Click += (_, _) =>
            {
                _answer = index;
                Close();
            };
            Buttons.Children.Add(button);
        }
    }

    /// <summary>Asks a question; returns the index of the answer chosen, or -1 if the dialog was
    /// closed without one. <paramref name="primary"/> is the highlighted answer.</summary>
    public static int Ask(Window? owner, string heading, string message, IReadOnlyList<string> answers, int primary = 0)
    {
        var dialog = new LayoutDialog(owner, heading, message, answers, primary);
        dialog.ShowDialog();
        return dialog._answer;
    }

    /// <summary>Asks for a name; null when cancelled.</summary>
    public static string? Prompt(Window? owner, string heading, string message, string initial, string confirm)
    {
        var dialog = new LayoutDialog(owner, heading, message, [confirm, "Cancel"], primary: 0);
        dialog.InputFrame.Visibility = Visibility.Visible;
        dialog.Input.Text = initial;
        dialog.Loaded += (_, _) =>
        {
            dialog.Input.Focus();
            dialog.Input.SelectAll();
        };
        dialog.ShowDialog();
        return dialog._answer == 0 ? dialog.Input.Text : null;
    }

    /// <summary>Tells the user something; one button to dismiss it.</summary>
    public static void Inform(Window? owner, string heading, string message) => Ask(owner, heading, message, ["OK"]);

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not TextBox)
        {
            DragMove();
        }
    }
}
