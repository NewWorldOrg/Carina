namespace Carina.Contracts.Tests;

public sealed class VersionSkewTests
{
    [Fact]
    public void AnAnswerWithoutTheNewerFieldsStillReads()
    {
        var session = DriverJson.Deserialize(
            """{"sessionId":"s-1","purpose":"recording","deviceId":"a0","state":"active","startedAt":"2026-08-08T21:04:00+09:00"}""",
            DriverJson.Context.SessionSnapshot
        );

        Assert.NotNull(session);
        Assert.Null(session.EndsAt);
        Assert.Equal(SessionPurpose.Recording, session.Purpose);
        Assert.Equal(SessionStopReason.Unspecified, session.StopReason);
        Assert.False(session.Concluded);
        Assert.Equal(SessionCounters.Nothing, session.Counters);
    }

    [Fact]
    public void ASessionWithoutCountersIsStillReadable()
    {
        var session = DriverJson.Deserialize(
            """{"sessionId":"s-1","purpose":"recording","deviceId":"a0","state":"stopped","startedAt":"2026-08-08T21:04:00+09:00","counters":null}""",
            DriverJson.Context.SessionSnapshot
        );

        Assert.NotNull(session);
        Assert.NotNull(session.Counters);
        Assert.False(session.Concluded);
    }

    [Fact]
    public void AnAnswerWithNewerFieldsAndValuesStillReads()
    {
        var session = DriverJson.Deserialize(
            """{"sessionId":"s-1","purpose":"epgNow","deviceId":"a0","state":"draining","startedAt":"2026-08-08T21:04:00+09:00","priority":8}""",
            DriverJson.Context.SessionSnapshot
        );

        Assert.NotNull(session);
        Assert.Equal(SessionPurpose.Unspecified, session.Purpose);
        Assert.Equal(SessionState.Unspecified, session.State);
        Assert.Equal("a0", session.DeviceId);
    }

    [Fact]
    public void AnUnknownTunerStateDoesNotReadAsAWorkingTuner()
    {
        var tuner = DriverJson.Deserialize(
            """{"deviceId":"a0","kind":"terrestrial","state":"warmingUp"}""",
            DriverJson.Context.TunerSnapshot
        );

        Assert.NotNull(tuner);
        Assert.Equal(TunerState.Unspecified, tuner.State);
        Assert.NotEqual(TunerState.Idle, tuner.State);
    }

    [Fact]
    public void NumericEnumValuesAreNotHonoured()
    {
        var tuner = DriverJson.Deserialize(
            """{"deviceId":"a0","kind":1,"state":2}""",
            DriverJson.Context.TunerSnapshot
        );

        Assert.NotNull(tuner);
        Assert.Equal(TunerKind.Unspecified, tuner.Kind);
        Assert.Equal(TunerState.Unspecified, tuner.State);
    }

    [Fact]
    public void ADriverWithoutACapabilityIsUsableForEverythingElse()
    {
        var hello = DriverJson.Deserialize(
            """{"protocolVersion":1,"instanceId":"old","capabilities":["recording"]}""",
            DriverJson.Context.DriverHello
        );

        Assert.NotNull(hello);
        Assert.True(hello.Supports(DriverCapabilities.Recording));
        Assert.False(hello.Supports(DriverCapabilities.QualityMetering));
    }

    [Fact]
    public void ARestartIsTheChangeOfInstanceAndNothingElse()
    {
        var first = new DriverHello(1, "b7f2c9", []);
        var reconnected = new DriverHello(1, "b7f2c9", [DriverCapabilities.Live]);
        var restarted = new DriverHello(1, "3ad901", []);

        Assert.False(reconnected.IsDifferentInstanceFrom(first));
        Assert.True(restarted.IsDifferentInstanceFrom(first));
        Assert.True(first.IsDifferentInstanceFrom(null));
    }

    [Fact]
    public void ADriverThatNamesNoInstanceIsAlwaysTreatedAsARestart()
    {
        var older = DriverJson.Deserialize(
            """{"protocolVersion":1,"capabilities":["recording"]}""",
            DriverJson.Context.DriverHello
        );

        Assert.NotNull(older);
        Assert.Null(older.InstanceId);
        Assert.True(older.IsDifferentInstanceFrom(older));
        Assert.True(older.IsDifferentInstanceFrom(new DriverHello(1, "b7f2c9", [])));
        Assert.True(new DriverHello(1, "b7f2c9", []).IsDifferentInstanceFrom(older));
    }

    [Fact]
    public void AnIdentifierOutsideTheShapeLeavesTheRestOfTheAnswerReadable()
    {
        var session = DriverJson.Deserialize(
            """{"sessionId":"../x","purpose":"live","deviceId":"a0","state":"active","startedAt":"2026-08-08T21:04:00+09:00"}""",
            DriverJson.Context.SessionSnapshot
        );

        Assert.NotNull(session);
        Assert.True(session.SessionId.IsUnset);
        Assert.Equal("a0", session.DeviceId);
        Assert.Equal(SessionState.Active, session.State);
    }

    [Fact]
    public void AnUnsetIdentifierHasNoPath()
    {
        Assert.Throws<ArgumentException>(() => DriverEndpoints.Session(default));
        Assert.Throws<ArgumentException>(() => DriverEndpoints.SessionStream(default));
    }
}
