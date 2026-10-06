namespace IRacingOverlay.Sdk.Tests;

public class SessionInfoParserTests
{
    private const string ValidYaml = """
        ---
        WeekendInfo:
         TrackName: spa 2024 gp
         TrackDisplayName: Circuit de Spa-Francorchamps
         SubSessionID: 12345
         TeamRacing: 1

        SessionInfo:
         Sessions:
         - SessionNum: 0
           SessionType: Race
           SessionName: RACE
           ResultsPositions:

        DriverInfo:
         DriverCarIdx: 0
         Drivers:
         - CarIdx: 0
           UserName: Carlos Test
           TeamName: Night Owls
           CarNumber: "7"
           CarClassEstLapTime: 115.5741
           IRating: 2500
         - CarIdx: 1
           UserName: Second Driver
           TeamName:
           CarNumber: "12"
           IRating: 1800

        ...
        """;

    [Fact]
    public void Parse_ValidYaml_IsNotDegraded()
    {
        var result = SessionInfoParser.Parse(ValidYaml);

        Assert.False(result.IsDegraded);
        Assert.Null(result.Error);
        Assert.Equal(12345, result.Session.WeekendInfo!.SubSessionID);
        Assert.Equal("Race", result.Session.SessionInfo!.Sessions[0].SessionType);
        Assert.Equal(2, result.Session.DriverInfo!.Drivers.Count);
    }

    [Fact]
    public void Parse_ReadsEachDriversEstLapTime()
    {
        var result = SessionInfoParser.Parse(ValidYaml);

        Assert.Equal(115.5741, result.Session.DriverInfo!.Drivers[0].CarClassEstLapTime, precision: 4);
    }

    [Fact]
    public void Parse_ReadsTheTireCompoundTable()
    {
        // The raw literal takes the checkout's line endings: CRLF on the Windows CI runner.
        var yaml = ValidYaml.ReplaceLineEndings("\n").Replace(
            " DriverCarIdx: 0\n",
            " DriverCarIdx: 0\n DriverTires:\n - TireIndex: 0\n   TireCompoundType: \"Hard\"\n - TireIndex: 1\n   TireCompoundType: \"Wet\"\n");

        var tires = SessionInfoParser.Parse(yaml).Session.DriverInfo!.DriverTires;

        Assert.Equal(["Hard", "Wet"], tires.Select(t => t.TireCompoundType));
        Assert.Equal([0, 1], tires.Select(t => t.TireIndex));
    }

    [Fact]
    public void Parse_ReadsTheStartingGrid()
    {
        // As a recorded race writes it: a top-level block, both positions 0-based.
        var yaml = ValidYaml.ReplaceLineEndings("\n").Replace(
            "\n...",
            "\nQualifyResultsInfo:\n Results:\n - Position: 0\n   ClassPosition: 0\n   CarIdx: 1\n   FastestLap: 0\n   FastestTime: -1.0000\n - Position: 1\n   ClassPosition: 1\n   CarIdx: 0\n   FastestLap: 0\n   FastestTime: -1.0000\n\n...");

        var grid = SessionInfoParser.Parse(yaml).Session.QualifyResultsInfo!.Results;

        Assert.Equal([1, 0], grid.Select(r => r.CarIdx));
        Assert.Equal([0, 1], grid.Select(r => r.Position));
        Assert.Equal([0, 1], grid.Select(r => r.ClassPosition));
    }

    [Fact]
    public void Parse_NoTireCompoundTable_IsEmpty()
    {
        Assert.Empty(SessionInfoParser.Parse(ValidYaml).Session.DriverInfo!.DriverTires);
    }

    [Fact]
    public void Parse_EmptyValues_BecomeEmptyCollectionsAndStrings()
    {
        var result = SessionInfoParser.Parse(ValidYaml);

        Assert.NotNull(result.Session.SessionInfo!.Sessions[0].ResultsPositions);
        Assert.Empty(result.Session.SessionInfo.Sessions[0].ResultsPositions);
        Assert.Equal("", result.Session.DriverInfo!.Drivers[1].TeamName);
    }

    [Theory]
    [InlineData("[OO] Racing")]
    [InlineData("Team: Speed")]
    [InlineData("#1 Motorsport")]
    [InlineData("{Curly} & *Star*")]
    [InlineData("Team #44")]
    public void Parse_UnquotedFreeText_KeepsTheTextExactly(string teamName)
    {
        var yaml = ValidYaml.Replace("TeamName: Night Owls", $"TeamName: {teamName}");

        var result = SessionInfoParser.Parse(yaml);

        Assert.False(result.IsDegraded);
        Assert.Null(result.Error);
        Assert.Equal(teamName, result.Session.DriverInfo!.Drivers[0].TeamName);
        Assert.Equal(12345, result.Session.WeekendInfo!.SubSessionID);
    }

    [Fact]
    public void Parse_UserNameWithQuotesAndBackslash_IsEscaped()
    {
        var yaml = ValidYaml.Replace("UserName: Carlos Test", "UserName: Carlos \"Fast\" Test: \\o/");

        var result = SessionInfoParser.Parse(yaml);

        Assert.Equal("Carlos \"Fast\" Test: \\o/", result.Session.DriverInfo!.Drivers[0].UserName);
    }

    [Fact]
    public void Parse_BrokenSectionOutsideFreeText_KeepsPreviousCopyOfThatSectionOnly()
    {
        var previous = SessionInfoParser.Parse(ValidYaml).Session;
        var yaml = ValidYaml
            .Replace("SessionName: RACE", "SessionName: RACE: BROKEN")
            .Replace("SubSessionID: 12345", "SubSessionID: 999");

        var result = SessionInfoParser.Parse(yaml, previous);

        Assert.Equal(["SessionInfo"], result.FailedSections);
        Assert.NotNull(result.Error);
        Assert.Same(previous.SessionInfo, result.Session.SessionInfo);
        // The healthy sections are the new ones, not the previous copy.
        Assert.Equal(999, result.Session.WeekendInfo!.SubSessionID);
        Assert.Equal(2, result.Session.DriverInfo!.Drivers.Count);
    }

    [Fact]
    public void Parse_NothingParseable_Throws()
    {
        Assert.Throws<InvalidDataException>(() => SessionInfoParser.Parse("WeekendInfo: [\nSessionInfo: {\nDriverInfo: ]\n"));
    }

    [Fact]
    public void Sanitize_LeavesAlreadyQuotedValuesAlone()
    {
        const string yaml = " - CarIdx: 0\n   UserName: \"Already Quoted\"\n";

        Assert.Equal(yaml, SessionInfoParser.Sanitize(yaml));
    }

    [Fact]
    public void IracingSessionInfoParse_UsesTheRepairingParser()
    {
        var yaml = ValidYaml.Replace("TeamName: Night Owls", "TeamName: [OO] Racing");

        var session = IracingSessionInfo.Parse(yaml);

        Assert.Equal("[OO] Racing", session.DriverInfo!.Drivers[0].TeamName);
    }

    [Theory]
    [InlineData("Spain", "Spain")]
    [InlineData("TÃ¼rkiye", "Türkiye")] // UTF-8 read as Windows-1252, as the session block arrives
    [InlineData("Hákon", "Hákon")] // genuine Windows-1252 text is left alone
    public void RepairUtf8_UndoesUtf8ReadAsWindows1252(string raw, string expected) =>
        Assert.Equal(expected, SessionInfoParser.RepairUtf8(raw));

    [Fact]
    public void Parse_ReadsEachDriversFlair()
    {
        var yaml = ValidYaml.Replace("IRating: 2500", "IRating: 2500\n   FlairName: Spain\n   FlairID: 198");

        var driver = SessionInfoParser.Parse(yaml).Session.DriverInfo!.Drivers[0];

        Assert.Equal("Spain", driver.FlairName);
        Assert.Equal(198, driver.FlairID);
    }
}
