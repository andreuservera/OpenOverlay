using IRacingOverlay.App.ControlPanel;
using IRacingOverlay.App.Diagnostics;

namespace IRacingOverlay.App.Tests;

public class ActivityTrailTests
{
    [Fact]
    public void SettingChanges_AreRecordedWithWhereTheyWereMade()
    {
        var label = "Trail " + Guid.NewGuid().ToString("N")[..8];
        var applied = new List<bool>();
        var toggle = new ToggleSetting(label, null, true, applied.Add) { TracePath = "Standings › Columns › " };
        var choice = new ChoiceSetting(label, null, ["Metric", "Imperial"], 0, _ => { }) { TracePath = "Units › Units › " };

        toggle.Value = false;
        toggle.Value = true;
        choice.SelectedIndex = 1;

        var trail = AppLog.Recent().Where(e => e.Message.Contains(label)).Select(e => e.Message).ToList();
        Assert.Equal(
        [
            $"Standings › Columns › {label}: off",
            $"Standings › Columns › {label}: on",
            $"Units › Units › {label}: Imperial",
        ], trail);
        Assert.Equal([false, true], applied);
    }

    [Fact]
    public void ChipChanges_IncludeTheirGroup()
    {
        var label = "Chip " + Guid.NewGuid().ToString("N")[..8];
        var chip = new ChipSetting(label, null, false, _ => { }) { TracePath = "Relative › Columns › Visible columns › " };

        chip.Value = true;

        Assert.Contains(AppLog.Recent(), e => e.Message == $"Relative › Columns › Visible columns › {label}: on");
    }
}
