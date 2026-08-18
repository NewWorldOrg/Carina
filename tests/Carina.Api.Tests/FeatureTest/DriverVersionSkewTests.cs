using System.Text.Json;

using Carina.Contracts;
using Carina.TestSupport;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class DriverVersionSkewTests
{
    private const string HelloFromABuildWithoutDraining =
        """{"protocolVersion":0,"instanceId":"instance-old","capabilities":["recording"]}""";

    private const string SessionsFromANewerBuild =
        """
        [{"sessionId":"rec-9","purpose":"epgNow","deviceId":"a0","state":"draining","startedAt":"2026-08-08T21:04:00+09:00","priority":8}]
        """;

    private const string SessionsThatAreNotJson = "{ this is not a driver answer";

    private static string[] StringsOf(JsonElement array)
        => [.. array.EnumerateArray().Select(element => element.GetString()!)];

    [Fact]
    public async Task AnOlderDriverKeepsWorkingAndTellsTheOperatorToUpdateIt()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(
            FakeDriver.HelloFor("instance-old", capabilities: ["recording"], protocolVersion: 0),
            driver => driver.RawBodyByPath[DriverEndpoints.Health] = HelloFromABuildWithoutDraining);

        JsonElement data = await feature.UntilConnectionIs("connected");
        JsonElement hello = data.GetProperty("hello");

        Assert.Equal(0, hello.GetProperty("protocolVersion").GetInt32());
        Assert.Equal("instance-old", hello.GetProperty("instanceId").GetString());
        Assert.Equal(["recording"], StringsOf(hello.GetProperty("capabilities")));
        Assert.False(hello.GetProperty("draining").GetBoolean());
        Assert.Equal(DriverProtocol.Version, data.GetProperty("appProtocolVersion").GetInt32());
        Assert.True(data.GetProperty("driverUpdateRequired").GetBoolean());
        Assert.Equal(["live"], StringsOf(data.GetProperty("missingCapabilities")));
        Assert.NotEqual(default, data.GetProperty("observedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task AnOlderProtocolOnItsOwnAlreadyAsksForAnUpdate()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(
            FakeDriver.HelloFor("instance-old", protocolVersion: 0));

        JsonElement data = await feature.UntilConnectionIs("connected");

        Assert.Equal(0, data.GetProperty("hello").GetProperty("protocolVersion").GetInt32());
        Assert.Empty(data.GetProperty("missingCapabilities").EnumerateArray());
        Assert.True(data.GetProperty("driverUpdateRequired").GetBoolean());
    }

    [Fact]
    public async Task AValueThisAppDoesNotKnowArrivesAsUnspecifiedRatherThanBreakingTheCall()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(
            FakeDriver.HelloFor("instance-a"),
            driver => driver.RawBodyByPath[DriverEndpoints.Sessions] = SessionsFromANewerBuild);

        await feature.UntilConnectionIs("connected");
        await feature.UntilReadoptions(1);

        SessionSnapshot session = Assert.Single(feature.Hook.LastSessions!);

        Assert.Equal("rec-9", session.SessionId.Value);
        Assert.Equal("a0", session.DeviceId);
        Assert.Equal(SessionPurpose.Unspecified, session.Purpose);
        Assert.Equal(SessionState.Unspecified, session.State);

        JsonElement data = await feature.StatusAsync();

        Assert.Equal("connected", DriverFeature.ConnectionOf(data));
    }

    [Fact]
    public async Task AnAnswerThatIsNotAContractAtAllIsNotReadoptedAsIfItWere()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(
            FakeDriver.HelloFor("instance-a"),
            driver => driver.RawBodyByPath[DriverEndpoints.Sessions] = SessionsThatAreNotJson);

        await Eventually.Happens(
            () => feature.Driver.RequestsFor(DriverEndpoints.Sessions) >= 2,
            "the supervisor asks a second time, so the first round is past its readoption");

        Assert.Equal(0, feature.Hook.CallCount);

        await feature.StatusAsync();
    }

    [Fact]
    public async Task AnEventNameThisAppDoesNotKnowDoesNotCostItTheFeed()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(FakeDriver.HelloFor("instance-a"));

        await feature.UntilConnectionIs("connected");
        await Eventually.Happens(
            () => feature.Driver.ListenerCount > 0,
            "the event feed is subscribed");

        feature.Driver.Signal("somethingFromTheFuture");
        feature.Driver.Signal(DriverEvents.Draining);

        JsonElement data = await feature.UntilConnectionIs("draining");

        Assert.Equal("instance-a", data.GetProperty("hello").GetProperty("instanceId").GetString());
    }
}
