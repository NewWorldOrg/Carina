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
    [Fact]
    public void HelloSerialisesToItsAgreedForm()
    {
        var json = DriverJson.Serialize(
            new DriverHello(1, [DriverCapabilities.Recording, DriverCapabilities.Live])
        );

        Assert.Equal(
            """{"protocolVersion":1,"capabilities":["recording","live"]}""",
            json
        );
    }

    [Fact]
    public void StartSessionRequestSerialisesToItsAgreedForm()
    {
        var json = DriverJson.Serialize(
            new StartSessionRequest(
                SessionPurpose.Recording,
                new TuningRequest(TunerKind.Terrestrial, 27, 1024),
                "adapter0"
            )
        );

        Assert.Equal(
            """{"purpose":"recording","tuning":{"kind":"terrestrial","physicalChannel":27,"serviceId":1024},"deviceId":"adapter0"}""",
            json
        );
    }

    [Fact]
    public void SessionSnapshotSerialisesToItsAgreedForm()
    {
        var json = DriverJson.Serialize(
            new SessionSnapshot(
                "s-1",
                SessionPurpose.Live,
                "adapter1",
                SessionState.Active,
                new DateTimeOffset(2026, 8, 8, 21, 4, 0, TimeSpan.FromHours(9))
            )
        );

        Assert.Equal(
            """{"sessionId":"s-1","purpose":"live","deviceId":"adapter1","state":"active","startedAt":"2026-08-08T21:04:00+09:00"}""",
            json
        );
    }

    [Fact]
    public void TunerSnapshotSerialisesToItsAgreedForm()
    {
        var json = DriverJson.Serialize(
            new TunerSnapshot("adapter2", TunerKind.Satellite, TunerState.Faulted, null, "kind mismatch")
        );

        Assert.Equal(
            """{"deviceId":"adapter2","kind":"satellite","state":"faulted","sessionId":null,"detail":"kind mismatch"}""",
            json
        );
    }

    // A newer driver sends fields this build has never heard of. Refusing to read
    // the rest of the answer would turn an additive change into a breaking one.
    [Fact]
    public void UnknownFieldsAreIgnored()
    {
        var hello = DriverJson.Deserialize(
            """{"protocolVersion":1,"capabilities":["recording"],"somethingNew":{"a":1}}""",
            DriverJson.Context.DriverHello
        );

        Assert.NotNull(hello);
        Assert.Equal(1, hello.ProtocolVersion);
        Assert.True(hello.Supports(DriverCapabilities.Recording));
    }

    // Enums travel as names so that a value added later reads as an unknown name
    // rather than silently landing on whichever member happens to share its number.
    [Fact]
    public void UnknownEnumNamesAreRejectedRatherThanGuessed()
    {
        Assert.Throws<JsonException>(
            () =>
                DriverJson.Deserialize(
                    """{"sessionId":"s-1","purpose":"telepathy","deviceId":"adapter0","state":"active","startedAt":"2026-08-08T21:04:00+09:00"}""",
                    DriverJson.Context.SessionSnapshot
                )
        );
    }

    [Fact]
    public void RoundTripKeepsTheValues()
    {
        var request = new StartSessionRequest(
            SessionPurpose.Survey,
            new TuningRequest(TunerKind.Satellite, 15)
        );

        var restored = DriverJson.Deserialize(
            DriverJson.Serialize(request),
            DriverJson.Context.StartSessionRequest
        );

        Assert.Equal(request, restored);
    }
}
