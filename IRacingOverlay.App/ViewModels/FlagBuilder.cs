using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>The driver-directed penalties a timing table shows on a car's row, each its own bit.</summary>
internal readonly record struct CarPenalties(bool Black, bool Meatball, bool Furled = false);

/// <summary>
/// Decodes iRacing's SessionFlags bitfield (irsdk_Flags) into every flag currently out. Pure
/// decoding: which of them are shown, and how, is <see cref="FlagPresenter"/>'s job. Bit values
/// match irsdk_defines.h.
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
                                  // Confirmed bug source: this was once treated as the meatball flag,
                                  // so it outranked Green around rolling starts. Deliberately ignored.
        Furled = 0x00080000,
        Repair = 0x00100000,     // the meatball flag: black with an orange dot — car damage, must pit
        DqScoringInvalid = 0x00200000, // disqualified and score card voided; Disqualify is set too
        StartHidden = 0x10000000,
        StartReady = 0x20000000,
        StartSet = 0x40000000,
        StartGo = 0x80000000,
    }

    public static List<ActiveFlag> Decode(TelemetrySnapshot telemetry) =>
        telemetry.HasVariable(TelemetryVarNames.SessionFlags) ? Decode(ReadFlagsBits(telemetry)) : [];

    public static List<ActiveFlag> Decode(uint raw)
    {
        var bits = (IrsdkFlags)raw;
        var flags = new List<ActiveFlag>();

        if (bits.HasFlag(IrsdkFlags.DqScoringInvalid))
        {
            flags.Add(new(FlagKind.Disqualified, FlagVariant.ScoreVoided));
        }
        else if (bits.HasFlag(IrsdkFlags.Disqualify))
        {
            flags.Add(new(FlagKind.Disqualified));
        }

        AddIf(IrsdkFlags.Black, FlagKind.Black);
        AddIf(IrsdkFlags.Repair, FlagKind.Meatball);
        AddIf(IrsdkFlags.Furled, FlagKind.Furled);
        AddIf(IrsdkFlags.Red, FlagKind.Red);
        AddIf(IrsdkFlags.Checkered, FlagKind.Checkered);
        AddIf(IrsdkFlags.White, FlagKind.White);

        if (bits.HasFlag(IrsdkFlags.CautionWaving))
        {
            flags.Add(new(FlagKind.Caution, FlagVariant.Waving));
        }
        else if (bits.HasFlag(IrsdkFlags.Caution))
        {
            flags.Add(new(FlagKind.Caution));
        }

        if (bits.HasFlag(IrsdkFlags.YellowWaving))
        {
            flags.Add(new(FlagKind.Yellow, FlagVariant.Waving));
        }
        else if (bits.HasFlag(IrsdkFlags.Yellow))
        {
            flags.Add(new(FlagKind.Yellow));
        }

        AddIf(IrsdkFlags.Debris, FlagKind.Debris);
        AddIf(IrsdkFlags.Blue, FlagKind.Blue);

        // OneLapToGreen means "still on the pace lap", never "green is out" — it gets its own board
        // rather than being folded into Green, which once showed the green flag before the start.
        AddIf(IrsdkFlags.OneLapToGreen, FlagKind.OneLapToGreen);

        if (bits.HasFlag(IrsdkFlags.Green) || bits.HasFlag(IrsdkFlags.GreenHeld) || bits.HasFlag(IrsdkFlags.StartGo))
        {
            flags.Add(new(FlagKind.Green));
        }
        else if (!bits.HasFlag(IrsdkFlags.StartHidden))
        {
            if (bits.HasFlag(IrsdkFlags.StartSet))
            {
                flags.Add(new(FlagKind.StartLights, FlagVariant.LightsSet));
            }
            else if (bits.HasFlag(IrsdkFlags.StartReady))
            {
                flags.Add(new(FlagKind.StartLights));
            }
        }

        if (bits.HasFlag(IrsdkFlags.FiveToGo))
        {
            flags.Add(new(FlagKind.FiveToGo));
        }
        else if (bits.HasFlag(IrsdkFlags.TenToGo))
        {
            flags.Add(new(FlagKind.TenToGo));
        }

        AddIf(IrsdkFlags.Crossed, FlagKind.Crossed);
        AddIf(IrsdkFlags.RandomWaving, FlagKind.RandomWaving);

        return flags;

        void AddIf(IrsdkFlags bit, FlagKind kind)
        {
            if (bits.HasFlag(bit))
            {
                flags.Add(new(kind));
            }
        }
    }

    /// <summary>
    /// Black, furled black and meatball flags per CarIdx, from CarIdxSessionFlags. The player's own
    /// SessionFlags is folded into their car, so their row is right even when the per-car array is missing.
    /// </summary>
    public static Func<int, CarPenalties> ReadCarPenalties(TelemetrySnapshot telemetry, int playerCarIdx)
    {
        var perCar = telemetry.HasVariable(TelemetryVarNames.CarIdxSessionFlags) ? ReadCarFlagsBits(telemetry) : null;
        var own = telemetry.HasVariable(TelemetryVarNames.SessionFlags) ? ReadFlagsBits(telemetry) : 0u;

        return carIdx =>
        {
            var bits = perCar is not null && carIdx >= 0 && carIdx < perCar.Length ? perCar[carIdx] : 0u;
            if (carIdx == playerCarIdx)
            {
                bits |= own;
            }

            var flags = (IrsdkFlags)bits;
            return new(flags.HasFlag(IrsdkFlags.Black), flags.HasFlag(IrsdkFlags.Repair), flags.HasFlag(IrsdkFlags.Furled));
        };
    }

    /// <summary>Decode and present with default options and no timing — what a fresh widget would
    /// show the instant these flags came out.</summary>
    public static IReadOnlyList<FlagState> Build(TelemetrySnapshot telemetry) =>
        FlagPresenter.Compose(Decode(telemetry), new FlagOptions());

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

    private static uint[] ReadCarFlagsBits(TelemetrySnapshot telemetry)
    {
        try
        {
            return telemetry.GetBitFieldArray(TelemetryVarNames.CarIdxSessionFlags);
        }
        catch (InvalidOperationException)
        {
            return Array.ConvertAll(telemetry.GetIntArray(TelemetryVarNames.CarIdxSessionFlags), v => unchecked((uint)v));
        }
    }
}
