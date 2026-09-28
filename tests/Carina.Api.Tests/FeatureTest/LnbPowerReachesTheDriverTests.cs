extern alias driver;

using System.Net;
using System.Net.Http.Json;
using System.Runtime.Versioning;
using System.Text.Json;

using driver::Carina.Driver.Configuration;

namespace Carina.Api.Tests.FeatureTest;

[SupportedOSPlatform("linux")]
public sealed class LnbPowerReachesTheDriverTests
{
    private const string Ground = "synthetic-terrestrial";

    private const string Sky = "synthetic-satellite";

    private static readonly Uri Tuners = new("/api/tuners", UriKind.Relative);

    [Fact]
    public async Task PowerSavedForASatelliteTunerIsWrittenToTheDriversLedgerAndIsOnOnceTheDriverStartsFromIt()
    {
        await using AppSwapFeature feature = await WithASatelliteTunerAsync();

        HttpStatusCode saved = await SaveAsync(feature, groundPower: false, skyPower: true);

        Assert.Equal(HttpStatusCode.OK, saved);
        Assert.True(Written(feature.Driver).Single(device => device.Id == Sky).LnbPower);
        Assert.False(Written(feature.Driver).Single(device => device.Id == Ground).LnbPower);

        JsonElement beforeTheRestart = await LedgerAsync(feature);

        Assert.True(beforeTheRestart.GetProperty("drifted").GetBoolean());
        Assert.True(DesiredPowerOf(beforeTheRestart, Sky));
        Assert.False(ObservedPowerOf(beforeTheRestart, Sky));

        await RestartFromTheLedgerAsync(feature);

        JsonElement afterTheRestart = await LedgerAsync(feature);

        Assert.False(afterTheRestart.GetProperty("drifted").GetBoolean());
        Assert.True(ObservedPowerOf(afterTheRestart, Sky));
        Assert.False(ObservedPowerOf(afterTheRestart, Ground));
    }

    [Fact]
    public async Task PowerAskedForATerrestrialTunerIsRefusedAndTheLedgerOnDiskIsLeftAsItWas()
    {
        await using AppSwapFeature feature = await WithASatelliteTunerAsync();
        string before = await File.ReadAllTextAsync(feature.Driver.LedgerPath);

        HttpStatusCode saved = await SaveAsync(feature, groundPower: true, skyPower: false);

        Assert.Equal(HttpStatusCode.BadRequest, saved);
        Assert.Equal(before, await File.ReadAllTextAsync(feature.Driver.LedgerPath));
    }

    [Fact]
    public async Task TurningThePowerOffAgainIsSavedAndLeavesTheSatelliteUnpoweredAfterTheRestart()
    {
        await using AppSwapFeature feature = await WithASatelliteTunerAsync();

        Assert.Equal(HttpStatusCode.OK, await SaveAsync(feature, groundPower: false, skyPower: true));

        await RestartFromTheLedgerAsync(feature);

        Assert.Equal(HttpStatusCode.OK, await SaveAsync(feature, groundPower: false, skyPower: false));
        Assert.False(Written(feature.Driver).Single(device => device.Id == Sky).LnbPower);

        await RestartFromTheLedgerAsync(feature);

        Assert.False(ObservedPowerOf(await LedgerAsync(feature), Sky));
    }

    private static async Task<AppSwapFeature> WithASatelliteTunerAsync()
    {
        AppSwapFeature feature = await AppSwapFeature.StartAsync();
        SyntheticDriverHost driver = feature.Driver;

        await driver.PutDownAsync();
        await feature.App.UntilConnectionIs("notConnected");

        driver.WriteLedger(driver.Configuration with
        {
            Devices = [new DeviceSettings(Ground, DeviceKind.Terrestrial), new DeviceSettings(Sky, DeviceKind.Satellite)],
        });

        await RestartFromTheLedgerAsync(feature);

        return feature;
    }

    private static async Task RestartFromTheLedgerAsync(AppSwapFeature feature)
    {
        await feature.Driver.PutDownAsync();
        await feature.App.UntilConnectionIs("notConnected");

        using StringWriter refusal = new();

        Assert.Equal(0, await feature.Driver.RaiseFromTheLedgerAsync(refusal));

        await feature.App.UntilConnectedAsync();
    }

    private static IReadOnlyList<DeviceSettings> Written(SyntheticDriverHost driver)
    {
        DriverConfiguration? written = DriverConfigurationReader.Parse(File.ReadAllText(driver.LedgerPath));

        Assert.NotNull(written);

        return written.Devices ?? [];
    }

    private static async Task<HttpStatusCode> SaveAsync(AppSwapFeature feature, bool groundPower, bool skyPower)
    {
        using HttpResponseMessage response = await feature.App.Client.PutAsJsonAsync(Tuners, new
        {
            tuners = new[]
            {
                new { deviceId = Ground, disabled = false, lnbPower = groundPower },
                new { deviceId = Sky, disabled = false, lnbPower = skyPower },
            },
        });

        return response.StatusCode;
    }

    private static async Task<JsonElement> LedgerAsync(AppSwapFeature feature)
    {
        using HttpResponseMessage response = await feature.App.Client.GetAsync(Tuners);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("data").Clone();
    }

    private static bool DesiredPowerOf(JsonElement ledger, string deviceId)
        => ledger.GetProperty("desired").EnumerateArray()
            .Single(entry => entry.GetProperty("deviceId").GetString() == deviceId)
            .GetProperty("lnbPower").GetBoolean();

    private static bool ObservedPowerOf(JsonElement ledger, string deviceId)
        => ledger.GetProperty("observed").EnumerateArray()
            .Single(entry => entry.GetProperty("deviceId").GetString() == deviceId)
            .GetProperty("lnbPowered").GetBoolean();
}
