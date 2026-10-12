using System.Globalization;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>
/// What a setting holds, as one comparable string, and how to put a setting back to another's value.
/// Kept out of the setting types so "is this changed?" works for every kind of row from one place,
/// by comparing a page's settings with the same page built from untouched options.
/// </summary>
internal static class SettingDefaults
{
    /// <summary>The setting's value as text, or null for a row that holds none (an action, a note).</summary>
    public static string? KeyOf(SettingItem item) => item switch
    {
        ToggleSetting toggle => toggle.Value.ToString(),
        ChoiceSetting choice => choice.SelectedIndex.ToString(CultureInfo.InvariantCulture),
        SegmentedSetting segmented => segmented.SelectedIndex.ToString(CultureInfo.InvariantCulture),
        NumberSetting number => number.Value.ToString("R", CultureInfo.InvariantCulture),
        SliderSetting slider => slider.Value.ToString("R", CultureInfo.InvariantCulture),
        ChipGroupSetting chips => string.Concat(chips.Chips.Select(chip => chip.Value ? '1' : '0')),
        ReorderListSetting list => string.Join(",", list.Items.Select(row =>
            $"{row.Key}:{(row.Visible.Value ? 1 : 0)}{(row.Companion is { } companion ? companion.Value ? "+" : "-" : "")}")),
        InfoBarSetting infoBar => string.Join(",", infoBar.Chips.Select(chip => infoBar.PlacementOf(chip.Element) is var (shown, slot)
            ? shown ? $"{chip.Element}@{slot}" : $"{chip.Element}:off"
            : "")),
        _ => null,
    };

    /// <summary>Gives <paramref name="item"/> the value <paramref name="from"/> holds, through the
    /// setting's own setters, so it is applied and saved exactly as a change by hand would be.</summary>
    public static void Reset(SettingItem item, SettingItem from)
    {
        switch (item, from)
        {
            case (ToggleSetting toggle, ToggleSetting source):
                toggle.Value = source.Value;
                break;
            case (ChoiceSetting choice, ChoiceSetting source):
                choice.SelectedIndex = source.SelectedIndex;
                break;
            case (SegmentedSetting segmented, SegmentedSetting source):
                segmented.SelectedIndex = source.SelectedIndex;
                break;
            case (NumberSetting number, NumberSetting source):
                // Through the box's text, so the box shows the value it was given.
                number.Text = source.Value.ToString(number.Format, CultureInfo.InvariantCulture);
                break;
            case (SliderSetting slider, SliderSetting source):
                slider.Value = source.Value;
                break;
            case (ChipGroupSetting chips, ChipGroupSetting source):
                for (var i = 0; i < chips.Chips.Count && i < source.Chips.Count; i++)
                {
                    chips.Chips[i].Value = source.Chips[i].Value;
                }

                break;
            case (ReorderListSetting list, ReorderListSetting source):
                list.SetOrder(source.Items.Select(row => row.Key));
                foreach (var row in list.Items)
                {
                    if (source.Items.FirstOrDefault(candidate => Equals(candidate.Key, row.Key)) is not { } original)
                    {
                        continue;
                    }

                    row.Visible.Value = original.Visible.Value;
                    if (row.Companion is { } companion && original.Companion is { } originalCompanion)
                    {
                        companion.Value = originalCompanion.Value;
                    }
                }

                break;
            case (InfoBarSetting infoBar, InfoBarSetting source):
                foreach (var chip in infoBar.Chips)
                {
                    var (shown, slot) = source.PlacementOf(chip.Element);
                    infoBar.Move(chip, shown ? slot : null);
                }

                break;
        }
    }
}
