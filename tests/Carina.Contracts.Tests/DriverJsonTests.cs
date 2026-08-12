using System.Text.Json;

namespace Carina.Contracts.Tests;

/// <summary>
/// The wire form is the contract. These expectations are written out in full so
/// that a change to a property name, a casing rule or an enum spelling fails here,
/// where it is a deliberate edit, rather than in production against a driver that
/// was built months earlier.
/// </summary>
public sealed class DriverJsonTests
{
    private static readonly DateTimeOffset Moment =
        new(2026, 8, 8, 21, 4, 0, TimeSpan.FromHours(9));

    [Fact]
    public void HelloSerialisesToItsAgreedForm()
    {
        var json = DriverJson.Serialize(
            new DriverHello(1, "b7f2c9", [DriverCapabilities.Recording, DriverCapabilities.Live])
        );

        Assert.Equal(
            """{"protocolVersion":1,"instanceId":"b7f2c9","capabilities":["recording","live"]}""",
            json
        );
    }

    [Fact]
    public void StartSessionRequestSerialisesToItsAgreedForm()
    {
        var json = DriverJson.Serialize(
            new StartSessionRequest
            {
                Purpose = SessionPurpose.Recording,
                Tuning = new TuningRequest(TunerKind.Terrestrial, 27, 1024),
                DeviceId = "adapter0",
                EndsAt = Moment,
            }
        );

        Assert.Equal(
            """{"purpose":"recording","tuning":{"kind":"terrestrial","physicalChannel":27,"serviceId":1024},"deviceId":"adapter0","endsAt":"2026-08-08T21:04:00+09:00"}""",
            json
        );
    }

    [Fact]
    public void SessionSnapshotSerialisesToItsAgreedForm()
    {
        var json = DriverJson.Serialize(
            new SessionSnapshot(
                SessionId.Parse("s-1"),
                SessionPurpose.Live,
                "adapter1",
                SessionState.Active,
                Moment
            )
        );

        Assert.Equal(
            """{"sessionId":"s-1","purpose":"live","state":"active","startedAt":"2026-08-08T21:04:00+09:00","endsAt":null,"deviceId":"adapter1"}""",
            json
        );
    }

    [Fact]
    public void TunerSnapshotSerialisesToItsAgreedForm()
    {
        var json = DriverJson.Serialize(
            new TunerSnapshot(
                "adapter2",
                TunerKind.Satellite,
                TunerState.Faulted,
                Detail: "kind mismatch"
            )
        );

        Assert.Equal(
            """{"kind":"satellite","state":"faulted","sessionId":null,"detail":"kind mismatch","deviceId":"adapter2"}""",
            json
        );
    }

    [Fact]
    public void DiagnosticSnapshotSerialisesToItsAgreedForm()
    {
        var json = DriverJson.Serialize(
            new DiagnosticSnapshot(
                DiagnosticReason.DiskSpaceLow,
                Moment,
                "adapter0",
                SessionId.Parse("s-1"),
                "3% left on the output volume"
            )
        );

        Assert.Equal(
            """{"reason":"diskSpaceLow","occurredAt":"2026-08-08T21:04:00+09:00","deviceId":"adapter0","sessionId":"s-1","detail":"3% left on the output volume"}""",
            json
        );
    }

    // These two are what GET /sessions and GET /tuners answer. A client written
    // against this contract has to know whether it is reading a bare array.
    [Fact]
    public void SessionListIsABareArray()
    {
        Assert.Equal(
            """[{"sessionId":"s-1","purpose":"live","state":"active","startedAt":"2026-08-08T21:04:00+09:00","endsAt":null,"deviceId":"adapter1"}]""",
            DriverJson.Serialize<IReadOnlyList<SessionSnapshot>>(
                [
                    new SessionSnapshot(
                        SessionId.Parse("s-1"),
                        SessionPurpose.Live,
                        "adapter1",
                        SessionState.Active,
                        Moment
                    ),
                ]
            )
        );

        Assert.Equal("[]", DriverJson.Serialize<IReadOnlyList<SessionSnapshot>>([]));
    }

    [Fact]
    public void TunerListIsABareArray()
    {
        Assert.Equal(
            """[{"kind":"terrestrial","state":"idle","sessionId":null,"detail":null,"deviceId":"adapter0"}]""",
            DriverJson.Serialize<IReadOnlyList<TunerSnapshot>>(
                [new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Idle)]
            )
        );

        Assert.Equal("[]", DriverJson.Serialize<IReadOnlyList<TunerSnapshot>>([]));
    }

    // Timestamps are ISO-8601 with an offset, and the offset is whatever the driver
    // runs in. A client that parses a fixed width, or expects a trailing Z, breaks
    // on the first driver that reports either of these.
    [Theory]
    [InlineData("2026-08-08T21:04:00.1234567+09:00")]
    [InlineData("2026-08-08T12:04:00+00:00")]
    public void TimestampsKeepWhateverPrecisionAndOffsetTheDriverReports(string wire)
    {
        var json =
            $$"""{"sessionId":"s-1","purpose":"live","deviceId":"a0","state":"active","startedAt":"{{wire}}","endsAt":null}""";

        var restored = DriverJson.Deserialize(json, DriverJson.Context.SessionSnapshot);

        Assert.NotNull(restored);
        Assert.Equal(DateTimeOffset.Parse(wire), restored.StartedAt);
    }

    // A newer driver sends fields this build has never heard of. Refusing to read
    // the rest of the answer would turn an additive change into a breaking one.
    [Fact]
    public void UnknownFieldsAreIgnored()
    {
        var hello = DriverJson.Deserialize(
            """{"protocolVersion":1,"instanceId":"b7f2c9","capabilities":["recording"],"somethingNew":{"a":1}}""",
            DriverJson.Context.DriverHello
        );

        Assert.NotNull(hello);
        Assert.Equal(1, hello.ProtocolVersion);
        Assert.True(hello.Supports(DriverCapabilities.Recording));
    }

    [Fact]
    public void RoundTripKeepsTheValues()
    {
        var request = new StartSessionRequest
        {
            Purpose = SessionPurpose.Survey,
            Tuning = new TuningRequest(TunerKind.Satellite, 15),
        };

        var restored = DriverJson.Deserialize(
            DriverJson.Serialize(request),
            DriverJson.Context.StartSessionRequest
        );

        Assert.Equal(request, restored);
    }

    // The members the driver acts on are not optional. Without this, an empty body
    // reads as a request with no tuning, and the purpose that blocks shutdown.
    [Fact]
    public void ARequestMissingItsMembersIsRejected()
    {
        Assert.Throws<JsonException>(
            () => DriverJson.Deserialize("{}", DriverJson.Context.StartSessionRequest)
        );
    }
}
