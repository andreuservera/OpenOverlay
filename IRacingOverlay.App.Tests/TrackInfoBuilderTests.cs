using IRacingOverlay.App.ViewModels;
using IRacingOverlay.Sdk;
using IRacingOverlay.Sdk.Interop;
using IRacingOverlay.Sdk.Tests;

namespace IRacingOverlay.App.Tests;

public class TrackInfoBuilderTests
{
    private static SyntheticMemoryBuilder TrackInfoVars()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("AirTemp", IrsdkVarType.Float);
        builder.AddVar("TrackTempCrew", IrsdkVarType.Float);
        builder.AddVar("WindVel", IrsdkVarType.Float);
        builder.AddVar("WindDir", IrsdkVarType.Float);
        builder.AddVar("RelativeHumidity", IrsdkVarType.Float);
        builder.AddVar("SessionTimeRemain", IrsdkVarType.Double);
        builder.AddVar("SessionLapsRemainEx", IrsdkVarType.Int);
        return builder;
    }

    private static IracingSessionInfo SessionWith(string trackDisplayShortName, string sessionName, int sessionNum, int currentSessionNum) => new()
    {
        WeekendInfo = new WeekendInfoSection { TrackDisplayShortName = trackDisplayShortName },
        SessionInfo = new SessionInfoSection
        {
            CurrentSessionNum = currentSessionNum,
            Sessions = [new SessionEntry { SessionNum = sessionNum, SessionName = sessionName, SessionType = "Race" }],
        },
    };

    [Fact]
    public void Build_ReadsTrackNameAndCurrentSessionLabel()
    {
        var builder = TrackInfoVars();
        var snapshot = TestSnapshotFactory.Build(builder, w => { });
        var session = SessionWith("Imola Full", "RACE", sessionNum: 0, currentSessionNum: 0);

        var state = TrackInfoBuilder.Build(snapshot, session);

        Assert.Equal("Imola Full", state.TrackNameDisplay);
        Assert.Equal("RACE", state.SessionLabelDisplay);
    }

    [Fact]
    public void Build_MultipleSessionsInWeekend_PicksTheCurrentOneNotTheFirst()
    {
        var builder = TrackInfoVars();
        var snapshot = TestSnapshotFactory.Build(builder, w => { });
        var session = new IracingSessionInfo
        {
            WeekendInfo = new WeekendInfoSection { TrackDisplayShortName = "Imola Full" },
            SessionInfo = new SessionInfoSection
            {
                CurrentSessionNum = 2,
                Sessions =
                [
                    new SessionEntry { SessionNum = 0, SessionName = "PRACTICE" },
                    new SessionEntry { SessionNum = 1, SessionName = "QUALIFY" },
                    new SessionEntry { SessionNum = 2, SessionName = "RACE" },
                ],
            },
        };

        var state = TrackInfoBuilder.Build(snapshot, session);

        Assert.Equal("RACE", state.SessionLabelDisplay);
    }

    [Fact]
    public void Build_ReadsLiveWeatherTelemetry()
    {
        var builder = TrackInfoVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetFloat("AirTemp", 26.1f);
            w.SetFloat("TrackTempCrew", 40.4f);
            w.SetFloat("WindVel", 0.9f);
            w.SetFloat("WindDir", 0f);
            w.SetFloat("RelativeHumidity", 0.45f);
        });

        var state = TrackInfoBuilder.Build(snapshot, session: null);

        Assert.Equal("26.1°C", state.AirTempDisplay);
        Assert.Equal("40.4°C", state.TrackTempDisplay);
        Assert.Equal("3.2 km/h N", state.WindDisplay);
        Assert.Equal("45%", state.HumidityDisplay);
    }

    [Theory]
    [InlineData(0f, "N")]
    [InlineData(MathF.PI / 2, "E")]
    [InlineData(MathF.PI, "S")]
    [InlineData(3 * MathF.PI / 2, "W")]
    [InlineData(MathF.PI / 4, "NE")]
    [InlineData(2 * MathF.PI, "N")]
    public void Build_WindDirectionRadians_MapsToCompassPoint(float radians, string expected)
    {
        var builder = TrackInfoVars();
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetFloat("WindDir", radians));

        var state = TrackInfoBuilder.Build(snapshot, session: null);

        Assert.Equal(expected, state.WindDirectionDisplay);
    }

    [Fact]
    public void Build_HumidityBelowHalf_DoesNotRoundToZero()
    {
        var builder = TrackInfoVars();
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetFloat("RelativeHumidity", 0.38f));

        var state = TrackInfoBuilder.Build(snapshot, session: null);

        Assert.Equal("38%", state.HumidityDisplay);
    }

    [Fact]
    public void Build_ReadsTrackUsageFromCurrentSession()
    {
        var builder = TrackInfoVars();
        var snapshot = TestSnapshotFactory.Build(builder, w => { });
        var session = new IracingSessionInfo
        {
            SessionInfo = new SessionInfoSection
            {
                CurrentSessionNum = 1,
                Sessions =
                [
                    new SessionEntry { SessionNum = 0, SessionName = "PRACTICE", SessionTrackRubberState = "clean" },
                    new SessionEntry { SessionNum = 1, SessionName = "RACE", SessionTrackRubberState = "moderately low usage" },
                ],
            },
        };

        var state = TrackInfoBuilder.Build(snapshot, session);

        Assert.Equal("MODERATELY LOW", state.TrackUsageDisplay);
    }

    [Theory]
    [InlineData("carry over", "CARRY OVER")]
    [InlineData("clean", "CLEAN")]
    [InlineData("high usage", "HIGH")]
    [InlineData("", "—")]
    public void Build_TrackUsagePhrasing_DropsRedundantUsageSuffix(string rubberState, string expected)
    {
        var builder = TrackInfoVars();
        var snapshot = TestSnapshotFactory.Build(builder, w => { });
        var session = new IracingSessionInfo
        {
            SessionInfo = new SessionInfoSection
            {
                CurrentSessionNum = 0,
                Sessions = [new SessionEntry { SessionNum = 0, SessionTrackRubberState = rubberState }],
            },
        };

        var state = TrackInfoBuilder.Build(snapshot, session);

        Assert.Equal(expected, state.TrackUsageDisplay);
    }

    [Theory]
    [InlineData("clean", 0)]
    [InlineData("very low usage", 1)]
    [InlineData("moderately low usage", 3)]
    [InlineData("moderately high usage", 5)]
    [InlineData("extreme usage", 7)]
    public void Build_TrackUsage_MapsOntoRubberScale(string rubberState, int expectedLevel)
    {
        var state = StateWithRubber(rubberState);

        Assert.Equal(expectedLevel, state.TrackUsageLevel);
        Assert.True(expectedLevel < TrackInfoState.TrackUsageLevelCount);
    }

    [Theory]
    [InlineData("carry over")]
    [InlineData("")]
    public void Build_TrackUsageOutsideScale_HasNoLevel(string rubberState)
    {
        Assert.Null(StateWithRubber(rubberState).TrackUsageLevel);
    }

    private static TrackInfoState StateWithRubber(string rubberState)
    {
        var snapshot = TestSnapshotFactory.Build(TrackInfoVars(), w => { });
        var session = new IracingSessionInfo
        {
            SessionInfo = new SessionInfoSection
            {
                CurrentSessionNum = 0,
                Sessions = [new SessionEntry { SessionNum = 0, SessionTrackRubberState = rubberState }],
            },
        };

        return TrackInfoBuilder.Build(snapshot, session);
    }

    [Fact]
    public void Build_TimeLimitedSession_ShowsCountdown()
    {
        var builder = TrackInfoVars();
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetDouble("SessionTimeRemain", 125.0));

        var state = TrackInfoBuilder.Build(snapshot, session: null);

        Assert.Equal("2:05", state.TimeRemainingDisplay);
    }

    [Fact]
    public void Build_LapLimitedSession_HidesTimeRemainingSentinel()
    {
        // iRacing reports an implausibly large number, not a sentinel like -1, for "no time limit."
        var builder = TrackInfoVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetDouble("SessionTimeRemain", 604_523_000.0);
            w.SetInt("SessionLapsRemainEx", 12);
        });

        var state = TrackInfoBuilder.Build(snapshot, session: null);

        Assert.Equal("—", state.TimeRemainingDisplay);
        Assert.Equal("12", state.LapsRemainingDisplay);
    }

    [Fact]
    public void Build_TimeLimitedSession_HidesLapsRemainingSentinel()
    {
        var builder = TrackInfoVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetDouble("SessionTimeRemain", 300.0);
            w.SetInt("SessionLapsRemainEx", 32767); // iRacing's "unlimited" sentinel
        });

        var state = TrackInfoBuilder.Build(snapshot, session: null);

        Assert.Equal("5:00", state.TimeRemainingDisplay);
        Assert.Equal("—", state.LapsRemainingDisplay);
    }

    [Fact]
    public void Build_MissingSession_StillReturnsWeatherWithDashesForTrackAndSession()
    {
        var builder = TrackInfoVars();
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetFloat("AirTemp", 22f));

        var state = TrackInfoBuilder.Build(snapshot, session: null);

        Assert.Equal("—", state.TrackNameDisplay);
        Assert.Equal("—", state.SessionLabelDisplay);
        Assert.Equal("22°C", state.AirTempDisplay);
    }
}
