namespace Carina.Contracts.Tests;

/// <summary>
/// Both pairings a deployment produces, in both directions.
/// </summary>
/// <remarks>
/// The two processes are released on independent tags, so an old driver talking to
/// a new app is the normal state, and the reverse happens for as long as it takes
/// to roll the app. Neither side may fail on what the other says; the contract only
/// grows, and what a build does not recognise it has to be able to ignore.
/// </remarks>
public sealed class VersionSkewTests
{
    // Old driver, new app: fields the newer build added are simply absent.
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
    }

    // New driver, old app: fields and enum values the older build never heard of.
    // The message stays readable and the unknown value degrades to "unspecified"
    // instead of landing on whichever member shares its position.
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

    // An ordinal says nothing about which member was meant. Honouring it would make
    // a value added later read as a value that exists today.
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

    // A driver older than the capability answers hello without it, and the app has
    // to carry on rather than treat the pairing as broken.
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

    // Only the instance identifies a run. A reconnection to the same process must
    // not look like a restart, or the app re-adopts sessions it should have kept.
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

    // A driver built before the field existed sends no instance at all. Reading two
    // absences as the same run would tell the app its sessions survived a restart
    // it cannot see — the one answer that must never be given by default.
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

    // An identifier this build would not have minted is still readable: losing the
    // whole answer over one session would take every other session with it.
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

    // What it cannot do is become a path: an unset id would address the collection.
    [Fact]
    public void AnUnsetIdentifierHasNoPath()
    {
        Assert.Throws<ArgumentException>(() => DriverEndpoints.Session(default));
        Assert.Throws<ArgumentException>(() => DriverEndpoints.SessionStream(default));
    }
}
