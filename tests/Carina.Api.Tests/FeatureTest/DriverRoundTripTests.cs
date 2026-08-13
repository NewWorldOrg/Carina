using System.Text.Json;

using Carina.Contracts;
using Carina.TestSupport;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class DriverRoundTripTests
{
    private static SessionSnapshot Recording(string id, string deviceId)
        => new(
            SessionId.Parse(id),
            SessionPurpose.Recording,
            deviceId,
            SessionState.Active,
            DateTimeOffset.UtcNow);

    private static string[] StringsOf(JsonElement array)
        => [.. array.EnumerateArray().Select(element => element.GetString()!)];

    [Fact]
    public async Task AnAuthenticatedRequestReachesTheDriverAcrossARealSocket()
    {
        await using var feature = await DriverFeature.StartAsync(
            FakeDriver.HelloFor("instance-a"),
            driver => driver.Sessions = [Recording("rec-1", "fake-terrestrial")]);

        var data = await feature.UntilConnectionIs("connected");
        var hello = data.GetProperty("hello");

        Assert.Equal("instance-a", hello.GetProperty("instanceId").GetString());
        Assert.Equal(DriverProtocol.Version, hello.GetProperty("protocolVersion").GetInt32());
        Assert.Equal(["recording", "live"], StringsOf(hello.GetProperty("capabilities")));
        Assert.False(hello.GetProperty("draining").GetBoolean());
        Assert.Equal(DriverProtocol.Version, data.GetProperty("appProtocolVersion").GetInt32());
        Assert.False(data.GetProperty("driverUpdateRequired").GetBoolean());
        Assert.Empty(data.GetProperty("missingCapabilities").EnumerateArray());

        await feature.UntilReadoptions(1);

        Assert.Equal("rec-1", Assert.Single(feature.Hook.LastSessions!).SessionId.Value);
    }

    [Fact]
    public async Task ADriverThatGoesAwayIsReportedAsNotConnectedAndNeverAsAFault()
    {
        await using var feature = await DriverFeature.StartAsync(FakeDriver.HelloFor("instance-a"));

        await feature.UntilConnectionIs("connected");
        await feature.StopDriverAsync();

        var data = await feature.UntilConnectionIs("notConnected");

        Assert.Equal(JsonValueKind.Null, data.GetProperty("hello").ValueKind);
        Assert.False(data.GetProperty("driverUpdateRequired").GetBoolean());
    }

    [Fact]
    public async Task ADriverThatComesBackAsANewInstanceFiresTheResyncHook()
    {
        await using var feature = await DriverFeature.StartAsync(
            FakeDriver.HelloFor("instance-a"),
            driver => driver.Sessions = [Recording("rec-1", "fake-terrestrial")]);

        await feature.UntilReadoptions(1);
        await feature.StopDriverAsync();
        await feature.UntilConnectionIs("notConnected");

        await feature.StartDriverAsync(
            FakeDriver.HelloFor("instance-b"),
            driver => driver.Sessions = [Recording("rec-2", "fake-satellite")]);

        await feature.UntilReadoptions(2);

        var data = await feature.UntilConnectionIs("connected");

        Assert.Equal(
            "instance-b",
            data.GetProperty("hello").GetProperty("instanceId").GetString());
        Assert.Equal("rec-2", Assert.Single(feature.Hook.LastSessions!).SessionId.Value);
    }

    [Fact]
    public async Task ADriverThatComesBackAsTheSameInstanceIsNotReadoptedAgain()
    {
        await using var feature = await DriverFeature.StartAsync(
            FakeDriver.HelloFor("instance-a"),
            driver => driver.Sessions = [Recording("rec-1", "fake-terrestrial")]);

        await feature.UntilReadoptions(1);
        await feature.StopDriverAsync();
        await feature.UntilConnectionIs("notConnected");

        await feature.StartDriverAsync(
            FakeDriver.HelloFor("instance-a"),
            driver => driver.Sessions = [Recording("rec-1", "fake-terrestrial")]);

        await feature.UntilConnectionIs("connected");
        await Eventually.Happens(
            () => feature.Driver.ListenerCount > 0,
            "the event feed is subscribed, which the supervisor only reaches past the readoption");

        Assert.Equal(1, feature.Hook.CallCount);
    }

    [Fact]
    public async Task TheAppStartsWithoutADriverAndAdoptsOneWhenItAppears()
    {
        await using var feature = await DriverFeature.StartAsync();

        var missing = await feature.UntilConnectionIs("notConnected");

        Assert.Equal(JsonValueKind.Null, missing.GetProperty("hello").ValueKind);

        await feature.StartDriverAsync(
            FakeDriver.HelloFor("instance-a"),
            driver => driver.Sessions = [Recording("rec-1", "fake-terrestrial")]);

        var data = await feature.UntilConnectionIs("connected");

        Assert.Equal(
            "instance-a",
            data.GetProperty("hello").GetProperty("instanceId").GetString());

        await feature.UntilReadoptions(1);

        Assert.Equal("rec-1", Assert.Single(feature.Hook.LastSessions!).SessionId.Value);
    }
}
