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
    // Standings only needs to feel "live," not sub-second precise — recomputing every 100ms was
    // wasted work (and, before the continuous-ordering fix, it happened to disguise a bug: since the
    // underlying data barely changed within a lap either way, it *looked* like updates only landed
    // at lap boundaries). This throttles it to roughly once a second without a second timer.
    private const int StandingsUpdateEveryNTicks = 10;

    private readonly IRacingConnection _connection = new();
    private readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly WheelSlipDetector _wheelSlipDetector = new();
    private int _tickCount;

    private RelativeWidget? _relativeWidget;
    private StandingsWidget? _standingsWidget;
    private CockpitWidget? _cockpitWidget;
    private FlagWidget? _flagWidget;
    private TireInfoWidget? _tireInfoWidget;
    private DeltaWidget? _deltaWidget;
    private DashboardWindow? _dashboard;
    private DeltaReference _deltaReference = DeltaReference.SessionBest;

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
            _cockpitWidget?.Close();
            _flagWidget?.Close();
            _tireInfoWidget?.Close();
            _deltaWidget?.Close();
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
        _relativeWidget?.UpdateRows(relativeRows);
        _dashboard?.UpdateRelativeRows(relativeRows);

        _tickCount++;
        if ((_standingsWidget is not null || _dashboard is not null) && _tickCount % StandingsUpdateEveryNTicks == 0)
        {
            var standingsRows = StandingsBuilder.BuildStandings(telemetry, session);
            _standingsWidget?.UpdateRows(standingsRows);
            _dashboard?.UpdateStandingsRows(standingsRows);
        }

        if (_cockpitWidget is not null || _dashboard is not null)
        {
            var cockpitState = CockpitBuilder.Build(telemetry, session, _wheelSlipDetector);
            _cockpitWidget?.UpdateState(cockpitState);
            _dashboard?.UpdateCockpit(cockpitState);
        }

        if (_flagWidget is not null || _dashboard is not null)
        {
            var flagStates = FlagBuilder.Build(telemetry);
            _flagWidget?.UpdateState(flagStates);
            _dashboard?.UpdateFlag(flagStates);
        }

        if (_tireInfoWidget is not null || _dashboard is not null)
        {
            var tireInfoState = TireInfoBuilder.Build(telemetry);
            _tireInfoWidget?.UpdateState(tireInfoState);
            _dashboard?.UpdateTireInfo(tireInfoState);
        }

        if (_deltaWidget is not null || _dashboard is not null)
        {
            var deltaState = DeltaBuilder.Build(telemetry, _deltaReference);
            _deltaWidget?.UpdateState(deltaState);
            _dashboard?.UpdateDelta(deltaState);
        }
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

    private void CockpitCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (CockpitCheckBox.IsChecked == true)
        {
            _cockpitWidget ??= new CockpitWidget();
            if (_cockpitWidget.HasSavedLayout)
            {
                _cockpitWidget.IsEditMode = EditModeCheckBox.IsChecked == true;
            }

            _cockpitWidget.Show();
        }
        else
        {
            _cockpitWidget?.Hide();
        }
    }

    private void FlagCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (FlagCheckBox.IsChecked == true)
        {
            _flagWidget ??= new FlagWidget();
            if (_flagWidget.HasSavedLayout)
            {
                _flagWidget.IsEditMode = EditModeCheckBox.IsChecked == true;
            }

            _flagWidget.Show();
        }
        else
        {
            _flagWidget?.Hide();
        }
    }

    private void TireInfoCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (TireInfoCheckBox.IsChecked == true)
        {
            _tireInfoWidget ??= new TireInfoWidget();
            if (_tireInfoWidget.HasSavedLayout)
            {
                _tireInfoWidget.IsEditMode = EditModeCheckBox.IsChecked == true;
            }

            _tireInfoWidget.Show();
        }
        else
        {
            _tireInfoWidget?.Hide();
        }
    }

    private void DeltaCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (DeltaCheckBox.IsChecked == true)
        {
            _deltaWidget ??= new DeltaWidget();
            if (_deltaWidget.HasSavedLayout)
            {
                _deltaWidget.IsEditMode = EditModeCheckBox.IsChecked == true;
            }

            _deltaWidget.Show();
        }
        else
        {
            _deltaWidget?.Hide();
        }
    }

    private void DeltaReferenceComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _deltaReference = DeltaReferenceComboBox.SelectedIndex switch
        {
            1 => DeltaReference.PersonalBestAllTime,
            2 => DeltaReference.OptimalLap,
            _ => DeltaReference.SessionBest,
        };
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

        if (_cockpitWidget is not null)
        {
            _cockpitWidget.IsEditMode = editMode;
        }

        if (_flagWidget is not null)
        {
            _flagWidget.IsEditMode = editMode;
        }

        if (_tireInfoWidget is not null)
        {
            _tireInfoWidget.IsEditMode = editMode;
        }

        if (_deltaWidget is not null)
        {
            _deltaWidget.IsEditMode = editMode;
        }
    }

    private void ShowDashboardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_dashboard is { IsVisible: true })
        {
            _dashboard.Hide();
            ShowDashboardButton.Content = "Show dashboard";
            return;
        }

        if (MonitorComboBox.SelectedItem is not Screen screen)
        {
            return;
        }

        if (_dashboard is null)
        {
            _dashboard = new DashboardWindow();
            _dashboard.Closed += (_, _) => ShowDashboardButton.Content = "Show dashboard";
        }

        _dashboard.MoveToScreen(screen);
        ShowDashboardButton.Content = "Hide dashboard";
    }
}
