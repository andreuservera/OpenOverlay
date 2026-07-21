using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Decodes iRacing's SessionFlags bitfield (irsdk_Flags) into a single most-relevant flag to
/// display. Bit values confirmed against the iRacing SDK reference documentation.
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

    public static FlagState Build(TelemetrySnapshot telemetry)
    {
        if (!telemetry.HasVariable(TelemetryVarNames.SessionFlags))
        {
            return FlagState.None;
        }

        var bits = (IrsdkFlags)ReadFlagsBits(telemetry);
        if (bits == 0)
        {
            return FlagState.None;
        }

        // Highest-priority flag wins when several are set at once (e.g. caution + yellowWaving).
        if (bits.HasFlag(IrsdkFlags.Disqualify))
        {
            return Solid("DISQUALIFIED", "#111111", "#FFFFFF");
        }

        if (bits.HasFlag(IrsdkFlags.Black))
        {
            return Solid("BLACK FLAG", "#111111", "#FFFFFF");
        }

        if (bits.HasFlag(IrsdkFlags.Repair))
        {
            return new FlagState { Name = "SERVICE", BackgroundColor = "#111111", ForegroundColor = "#FF8C1A", IsCheckered = false, IsMeatball = true };
        }

        if (bits.HasFlag(IrsdkFlags.Furled))
        {
            return Solid("WARNING", "#2A2A2A", "#FF8800");
        }

        // Servicible is deliberately never checked here — it isn't a flag (see enum comment above).

        if (bits.HasFlag(IrsdkFlags.Red))
        {
            return Solid("RED", "#CC1414", "#FFFFFF");
        }

        if (bits.HasFlag(IrsdkFlags.Checkered))
        {
            return new FlagState { Name = "CHECKERED", BackgroundColor = "#FFFFFF", ForegroundColor = "#111111", IsCheckered = true, IsMeatball = false };
        }

        if (bits.HasFlag(IrsdkFlags.White))
        {
            return Solid("WHITE — LAST LAP", "#FFFFFF", "#111111");
        }

        if (bits.HasFlag(IrsdkFlags.CautionWaving) || bits.HasFlag(IrsdkFlags.YellowWaving))
        {
            return Solid("CAUTION", "#E8C000", "#111111");
        }

        if (bits.HasFlag(IrsdkFlags.Caution) || bits.HasFlag(IrsdkFlags.Yellow))
        {
            return Solid("YELLOW", "#E8C000", "#111111");
        }

        if (bits.HasFlag(IrsdkFlags.Debris))
        {
            return Solid("DEBRIS", "#E8C000", "#111111");
        }

        if (bits.HasFlag(IrsdkFlags.Blue))
        {
            return Solid("BLUE — FASTER CAR", "#1560D4", "#FFFFFF");
        }

        if (bits.HasFlag(IrsdkFlags.Green) || bits.HasFlag(IrsdkFlags.GreenHeld) || bits.HasFlag(IrsdkFlags.OneLapToGreen) || bits.HasFlag(IrsdkFlags.StartGo))
        {
            return Solid("GREEN", "#1FA028", "#FFFFFF");
        }

        if (bits.HasFlag(IrsdkFlags.StartSet))
        {
            return Solid("SET", "#E8C000", "#111111");
        }

        if (bits.HasFlag(IrsdkFlags.StartReady))
        {
            return Solid("READY", "#FFFFFF", "#111111");
        }

        return FlagState.None;
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
        IsCheckered = false,
        IsMeatball = false,
    };
}
