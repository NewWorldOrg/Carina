extern alias driver;

using System.Net;
using System.Net.Http.Json;
using System.Runtime.Versioning;
using System.Text.Json;

using Carina.Contracts;
using Carina.Domain.Channels;

using driver::Carina.Driver.Configuration;

namespace Carina.Api.Tests.FeatureTest;

[SupportedOSPlatform("linux")]
public sealed class LnbPowerReachesTheDriverTests
{
    private const string Ground = "synthetic-terrestrial";

    private const string Sky = "synthetic-satellite";

    private static readonly Uri Tuners = new("/api/tuners", UriKind.Relative);

    [Fact]
    public async Task PowerSwitchedForASatelliteTunerIsWrittenToTheDriversLedgerAndIsOnOnceTheDriverStartsFromIt()
    {
        await using AppSwapFeature feature = await WithASatelliteTunerAsync();

        Assert.Equal(HttpStatusCode.OK, await SwitchAsync(feature, Sky, true));
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
    public async Task SwitchingThePowerWritesThatOneFlagAndLeavesEverythingElseTheLedgerSaysAlone()
    {
        await using AppSwapFeature feature = await WithASatelliteTunerAsync();
        feature.Driver.WriteLedger(feature.Driver.Configuration with
        {
            Devices =
            [
                new DeviceSettings(Ground, DeviceKind.Terrestrial, Enabled: false),
                new DeviceSettings(Sky, DeviceKind.Satellite),
            ],
        });

        Assert.Equal(HttpStatusCode.OK, await SwitchAsync(feature, Sky, true));

        IReadOnlyList<DeviceSettings> written = Written(feature.Driver);

        Assert.False(written.Single(device => device.Id == Ground).Enabled);
        Assert.True(written.Single(device => device.Id == Sky).Enabled);
        Assert.True(written.Single(device => device.Id == Sky).LnbPower);
    }

    [Fact]
    public async Task SwitchingThePowerAsksNoCandidateToProveItselfAgainAndSettlesNoAllocationAgain()
    {
        await using AppSwapFeature feature = await WithASatelliteTunerAsync();
        feature.Candidates.Candidates.Add(CandidateChannel.Discover(
            CandidateChannelId.New(),
            new NetworkId(1),
            new ServiceId(101),
            TuningParameters.Terrestrial(53),
            DateTime.UtcNow));
        int nudgedBefore = feature.Notices.Nudged.Count;

        Assert.Equal(HttpStatusCode.OK, await SwitchAsync(feature, Sky, true));
        Assert.Equal(HttpStatusCode.OK, await SwitchAsync(feature, Sky, false));

        Assert.False(Assert.Single(feature.Candidates.Candidates).NeedsRevalidation);
        Assert.Equal(nudgedBefore, feature.Notices.Nudged.Count);
    }

    [Fact]
    public async Task PowerAskedForATerrestrialTunerIsRefusedAndTheLedgerOnDiskIsLeftAsItWas()
    {
        await using AppSwapFeature feature = await WithASatelliteTunerAsync();
        string before = await File.ReadAllTextAsync(feature.Driver.LedgerPath);

        Assert.Equal(HttpStatusCode.BadRequest, await SwitchAsync(feature, Ground, true));
        Assert.Equal(before, await File.ReadAllTextAsync(feature.Driver.LedgerPath));
    }

    [Fact]
    public async Task PowerForATunerTheLedgerDoesNotHoldIsAnsweredAsNotFound()
    {
        await using AppSwapFeature feature = await WithASatelliteTunerAsync();
        string before = await File.ReadAllTextAsync(feature.Driver.LedgerPath);

        Assert.Equal(HttpStatusCode.NotFound, await SwitchAsync(feature, "synthetic-elsewhere", true));
        Assert.Equal(before, await File.ReadAllTextAsync(feature.Driver.LedgerPath));
    }

    [Fact]
    public async Task TurningThePowerOffAgainIsSavedAndLeavesTheSatelliteUnpoweredAfterTheRestart()
    {
        await using AppSwapFeature feature = await WithASatelliteTunerAsync();

        Assert.Equal(HttpStatusCode.OK, await SwitchAsync(feature, Sky, true));

        await RestartFromTheLedgerAsync(feature);

        Assert.Equal(HttpStatusCode.OK, await SwitchAsync(feature, Sky, false));
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

    private static async Task<HttpStatusCode> SwitchAsync(AppSwapFeature feature, string deviceId, bool on)
    {
        using HttpResponseMessage response = await feature.App.Client.PutAsJsonAsync(
            new Uri($"/api/tuners/{deviceId}/lnb-power", UriKind.Relative),
            new { lnbPower = on });

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
