using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

/// <summary>
/// Draws exactly the flags it is given, most important first — which flags those are is decided by
/// <see cref="FlagPresenter"/>, and whether an empty list shows a placeholder is up to the host
/// (the widget only does in edit mode; the dashboard always does).
/// </summary>
public partial class FlagPanel : UserControl
{
    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
        nameof(Options), typeof(FlagOptions), typeof(FlagPanel),
        new PropertyMetadata(new FlagOptions()));

    public FlagPanel()
    {
        InitializeComponent();
        Frame.Visibility = Visibility.Collapsed;
    }

    public ObservableCollection<FlagState> Flags { get; } = [];

    public FlagOptions Options
    {
        get => (FlagOptions)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    public void UpdateState(IReadOnlyList<FlagState> flags)
    {
        // Flags change a few times a race: leave the visual tree alone on every tick they don't.
        if (flags.Count == Flags.Count && flags.Select(f => f.Key).SequenceEqual(Flags.Select(f => f.Key)))
        {
            return;
        }

        for (var i = 0; i < flags.Count; i++)
        {
            if (i < Flags.Count)
            {
                Flags[i] = flags[i];
            }
            else
            {
                Flags.Add(flags[i]);
            }
        }

        while (Flags.Count > flags.Count)
        {
            Flags.RemoveAt(Flags.Count - 1);
        }

        Frame.Visibility = Flags.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
