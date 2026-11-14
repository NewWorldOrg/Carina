using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;

using Puppis.Contracts;

using static Carina.Api.Tests.Feature.Tuners.TunerLedgerFeature;

namespace Carina.Api.Tests.Feature.Tuners;

[Trait("Category", "Feature")]
public sealed class GetDetectedTunersActionTests
{
    [Fact]
    public async Task ADetectedDeviceCarriesItsHardware()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(HardwareStock.Capable(), HardwareStock.Stocked);

        using HttpResponseMessage response = await feature.Client.GetAsync(Detected);
        (HttpStatusCode status, JsonElement body) = await DriverFeature.ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, status);
        JsonElement device = body.GetProperty("data").GetProperty("devices")[0];
        Assert.Equal("Example ISDB-T frontend", device.GetProperty("hardware").GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, device.GetProperty("machine").ValueKind);
    }

    [Fact]
    public async Task DetectionNamesWhatIsNewWhatIsGoneAndWhatChangedItsKind()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), driver =>
        {
            Stocked(driver);
            driver.DetectedDevices =
            [
                new DetectedDeviceDto
                {
                    DeviceId = "adapter0",
                    Detection = DeviceDetection.Detected,
                    Kinds = [TunerKind.Satellite],
                },
                new DetectedDeviceDto
                {
                    DeviceId = "adapter2",
                    Detection = DeviceDetection.Detected,
                    Kinds = [TunerKind.Terrestrial],
                },
            ];
        });

        (HttpStatusCode status, JsonElement body) = await DriverFeature.ReadAsync(await feature.Client.GetAsync(Detected));
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            ["adapter2"],
            data.GetProperty("added").EnumerateArray().Select(entry => entry.GetString()));
        Assert.Equal(
            ["adapter1"],
            data.GetProperty("missing").EnumerateArray().Select(entry => entry.GetString()));
        Assert.Equal(
            ["adapter0"],
            data.GetProperty("mismatched").EnumerateArray()
                .Select(entry => entry.GetProperty("deviceId").GetString()));
    }

    [Fact]
    public async Task AnUndisturbedLedgerShowsNoDetectionDifference()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        (HttpStatusCode _, JsonElement body) = await DriverFeature.ReadAsync(await feature.Client.GetAsync(Detected));
        JsonElement data = body.GetProperty("data");

        Assert.Equal(0, data.GetProperty("added").GetArrayLength());
        Assert.Equal(0, data.GetProperty("missing").GetArrayLength());
        Assert.Equal(0, data.GetProperty("mismatched").GetArrayLength());
    }

    [Fact]
    public async Task AMachineWithNothingConfiguredIsSetUpThroughTheApiAndNowhereElse()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Unconfigured);

        (HttpStatusCode looked, JsonElement found) = await DriverFeature.ReadAsync(await feature.Client.GetAsync(Detected));
        JsonElement difference = found.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, looked);
        Assert.Equal(
            ["adapter0", "adapter1"],
            difference.GetProperty("added").EnumerateArray().Select(entry => entry.GetString()));
        Assert.Equal(0, difference.GetProperty("missing").GetArrayLength());
    }

    [Fact]
    public async Task EveryLedgerSurfaceIsBehindTheSameDenialAsTheRestOnceASchemeIsRegistered()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);
        using var app = new TestingWebApplicationFactory();
        using HttpClient client = app.WithTestScheme().CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Detected)).StatusCode);
    }
}
