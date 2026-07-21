using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Decodes iRacing's SessionFlags bitfield (irsdk_Flags) into the set of currently-relevant flags to
/// display. Bit values confirmed against the iRacing SDK reference documentation. Returns a list
/// because multiple flags can legitimately be active at once — e.g. a full-course caution for debris,
/// or a blue "car behind" call during green-flag racing — and iRacing's own flag panel shows them
/// simultaneously rather than picking just one.
/// </summary>
internal static class FlagBuilder
{
    [Flags]
    private enum IrsdkFlags : uint
    {
        Checkered = 0x00000001,
        White = 0x00000002,
        Green = 0x00000004,
        Yellow = 0x00000008,
        Red = 0x00000010,
        Blue = 0x00000020,
        Debris = 0x00000040,
        Crossed = 0x00000080,
        YellowWaving = 0x00000100,
        OneLapToGreen = 0x00000200,
        GreenHeld = 0x00000400,
        TenToGo = 0x00000800,
        FiveToGo = 0x00001000,
        RandomWaving = 0x00002000,
        Caution = 0x00004000,
        CautionWaving = 0x00008000,
        Black = 0x00010000,      // "Client has a black (penalty) flag" — directed at the local driver
        Disqualify = 0x00020000, // "Client has been disqualified"
        Servicible = 0x00040000, // NOT a flag — official SDK comment: "car is allowed service".
                                  // Confirmed bug source: this was wrongly treated as the meatball
                                  // flag, so it could outrank Green whenever it happened to be set
                                  // (e.g. around a rolling start), showing "SERVICE" after the green
                                  // flag had already dropped. The real meatball/repair flag is below.
        Furled = 0x00080000,
        Repair = 0x00100000,     // the meatball flag: black with an orange dot — car damage, must pit
        StartHidden = 0x10000000,
        StartReady = 0x20000000,
        StartSet = 0x40000000,
        StartGo = 0x80000000,
    }

    public static List<FlagState> Build(TelemetrySnapshot telemetry)
    {
        if (!telemetry.HasVariable(TelemetryVarNames.SessionFlags))
        {
            return [];
        }

        var bits = (IrsdkFlags)ReadFlagsBits(telemetry);
        if (bits == 0)
        {
            return [];
        }

        var flags = new List<FlagState>();

        // Primary track-state flags: mutually exclusive (the race is never simultaneously "green" and
        // "checkered"), so only the single most severe one is shown — highest priority first.
        if (bits.HasFlag(IrsdkFlags.Disqualify))
        {
            flags.Add(Solid("DISQUALIFIED", "#111111", "#FFFFFF"));
        }
        else if (bits.HasFlag(IrsdkFlags.Black))
        {
            flags.Add(Solid("BLACK FLAG", "#111111", "#FFFFFF"));
        }
        else if (bits.HasFlag(IrsdkFlags.Repair))
        {
            flags.Add(new FlagState { Name = "SERVICE", BackgroundColor = "#111111", ForegroundColor = "#FF8C1A", Style = FlagVisualStyle.Meatball });
        }
        else if (bits.HasFlag(IrsdkFlags.Furled))
        {
            flags.Add(Solid("WARNING", "#2A2A2A", "#FF8800"));
        }
        else if (bits.HasFlag(IrsdkFlags.Red))
        {
            flags.Add(Solid("RED", "#CC1414", "#FFFFFF"));
        }
        else if (bits.HasFlag(IrsdkFlags.Checkered))
        {
            flags.Add(new FlagState { Name = "CHECKERED", BackgroundColor = "#FFFFFF", ForegroundColor = "#111111", Style = FlagVisualStyle.Checkered });
        }
        else if (bits.HasFlag(IrsdkFlags.White))
        {
            flags.Add(Solid("WHITE — LAST LAP", "#FFFFFF", "#111111"));
        }
        else if (bits.HasFlag(IrsdkFlags.CautionWaving) || bits.HasFlag(IrsdkFlags.Caution))
        {
            flags.Add(Solid("CAUTION", "#E8C000", "#111111"));
        }
        else if (bits.HasFlag(IrsdkFlags.YellowWaving) || bits.HasFlag(IrsdkFlags.Yellow))
        {
            flags.Add(Solid("LOCAL YELLOW", "#E8C000", "#111111"));
        }
        else if (bits.HasFlag(IrsdkFlags.Green) || bits.HasFlag(IrsdkFlags.GreenHeld) || bits.HasFlag(IrsdkFlags.OneLapToGreen) || bits.HasFlag(IrsdkFlags.StartGo))
        {
            flags.Add(Solid("GREEN", "#1FA028", "#FFFFFF"));
        }
        else if (bits.HasFlag(IrsdkFlags.StartSet))
        {
            flags.Add(Solid("SET", "#E8C000", "#111111"));
        }
        else if (bits.HasFlag(IrsdkFlags.StartReady))
        {
            flags.Add(Solid("READY", "#FFFFFF", "#111111"));
        }

        // Secondary/supplementary flags — these can legitimately coexist with any primary state above
        // (a blue "faster car behind" call can happen mid-caution or mid-green; debris often
        // accompanies but is distinct from a caution), so they're independent checks, not part of the
        // priority chain, and both can appear alongside the primary flag and each other.
        if (bits.HasFlag(IrsdkFlags.Debris))
        {
            // The real "surface" flag: yellow and red diagonal stripes, not a plain solid color.
            flags.Add(new FlagState { Name = "DEBRIS", BackgroundColor = "#E8C000", ForegroundColor = "#111111", Style = FlagVisualStyle.DebrisStripes });
        }

        if (bits.HasFlag(IrsdkFlags.Blue))
        {
            // Real-world blue flag has a diagonal orange stripe, not just a solid blue field.
            flags.Add(new FlagState { Name = "BLUE — CAR BEHIND", BackgroundColor = "#1560D4", ForegroundColor = "#FFFFFF", Style = FlagVisualStyle.BlueWithOrangeStripe });
        }

        return flags;
    }

    private static uint ReadFlagsBits(TelemetrySnapshot telemetry)
    {
        // SessionFlags is a genuine bitmask (multiple flags combine, powers of two), unlike
        // CarLeftRight which turned out to be mislabeled — but read defensively anyway given that
        // exact lesson, rather than assume the var-header type without a fallback.
        try
        {
            return telemetry.GetBitField(TelemetryVarNames.SessionFlags);
        }
        catch (InvalidOperationException)
        {
            return unchecked((uint)telemetry.GetInt(TelemetryVarNames.SessionFlags));
        }
    }

    private static FlagState Solid(string name, string background, string foreground) => new()
    {
        Name = name,
        BackgroundColor = background,
        ForegroundColor = foreground,
        Style = FlagVisualStyle.Solid,
    };
}
