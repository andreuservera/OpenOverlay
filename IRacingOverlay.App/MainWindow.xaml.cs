using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using IRacingOverlay.App.Dashboard;
using IRacingOverlay.App.ViewModels;
using IRacingOverlay.App.Widgets;
using IRacingOverlay.Sdk;
using Screen = System.Windows.Forms.Screen;

namespace IRacingOverlay.App;

public partial class MainWindow : Window
{
    private readonly IRacingConnection _connection = new();
    private readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    private RelativeWidget? _relativeWidget;
    private StandingsWidget? _standingsWidget;
    private DashboardWindow? _dashboard;

    public MainWindow()
    {
        InitializeComponent();

        MonitorComboBox.ItemsSource = Screen.AllScreens;
        MonitorComboBox.DisplayMemberPath = "DeviceName";
        MonitorComboBox.SelectedIndex = Screen.AllScreens.Length > 1 ? 1 : 0;

        _connection.Connected += (_, _) => Dispatcher.BeginInvoke(() => SetStatus(connected: true));
        _connection.Disconnected += (_, _) => Dispatcher.BeginInvoke(() => SetStatus(connected: false));

        _uiTimer.Tick += UiTimer_Tick;
        _uiTimer.Start();

        Closed += (_, _) =>
        {
            _uiTimer.Stop();
            _connection.Stop();
            _relativeWidget?.Close();
            _standingsWidget?.Close();
            _dashboard?.Close();
        };

        _connection.Start();
    }

    private void SetStatus(bool connected)
    {
        StatusDot.Fill = connected ? Brushes.LimeGreen : Brushes.Red;
        StatusText.Text = connected ? "Connected to iRacing" : "Waiting for iRacing…";
    }

    private void UiTimer_Tick(object? sender, EventArgs e)
    {
        var telemetry = _connection.Latest;
        if (telemetry is null)
        {
            return;
        }

        UpdateDebugText(telemetry);

        var session = _connection.Session;
        var relativeRows = StandingsBuilder.BuildRelative(telemetry, session);
        var standingsRows = StandingsBuilder.BuildStandings(telemetry, session);

        _relativeWidget?.UpdateRows(relativeRows);
        _standingsWidget?.UpdateRows(standingsRows);
        _dashboard?.UpdateRows(standingsRows, relativeRows);
    }

    private void UpdateDebugText(TelemetrySnapshot telemetry)
    {
        var speed = telemetry.HasVariable("Speed") ? telemetry.GetFloat("Speed") * 3.6 : 0; // m/s -> km/h
        var lap = telemetry.HasVariable("Lap") ? telemetry.GetInt("Lap") : 0;
        var gear = telemetry.HasVariable("Gear") ? telemetry.GetInt("Gear") : 0;
        DebugText.Text = $"Speed: {speed:0} km/h   Lap: {lap}   Gear: {gear}";
    }

    private void RelativeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (RelativeCheckBox.IsChecked == true)
        {
            _relativeWidget ??= new RelativeWidget();
            // A brand-new widget (no saved position yet) starts in edit mode so it can be placed;
            // only force it from the checkbox once it actually has a position worth locking.
            if (_relativeWidget.HasSavedLayout)
            {
                _relativeWidget.IsEditMode = EditModeCheckBox.IsChecked == true;
            }

            _relativeWidget.Show();
        }
        else
        {
            _relativeWidget?.Hide();
        }
    }

    private void StandingsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (StandingsCheckBox.IsChecked == true)
        {
            _standingsWidget ??= new StandingsWidget();
            if (_standingsWidget.HasSavedLayout)
            {
                _standingsWidget.IsEditMode = EditModeCheckBox.IsChecked == true;
            }

            _standingsWidget.Show();
        }
        else
        {
            _standingsWidget?.Hide();
        }
    }

    private void EditModeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        var editMode = EditModeCheckBox.IsChecked == true;
        if (_relativeWidget is not null)
        {
            _relativeWidget.IsEditMode = editMode;
        }

        if (_standingsWidget is not null)
        {
            _standingsWidget.IsEditMode = editMode;
        }
    }

    private void ShowDashboardButton_Click(object sender, RoutedEventArgs e)
    {
        if (MonitorComboBox.SelectedItem is not Screen screen)
        {
            return;
        }

        _dashboard ??= new DashboardWindow();
        _dashboard.MoveToScreen(screen);
    }
}
