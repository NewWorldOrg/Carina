extern alias driver;

using System.Net;
using System.Net.Http.Json;
using System.Runtime.Versioning;
using System.Text.Json;

using driver::Carina.Driver.Configuration;

namespace Carina.Api.Tests.FeatureTest;

[SupportedOSPlatform("linux")]
public sealed class LedgerSavedFromAnOldReadingTests
{
    private const string Ground = "synthetic-terrestrial";

    private const string Sky = "synthetic-satellite";

    private static readonly Uri Tuners = new("/api/tuners", UriKind.Relative);

    [Fact]
    public async Task ASaveCarryingTheLedgerItReadIsTakenWhileTheLedgerIsStillThatOne()
    {
        await using AppSwapFeature feature = await WithASatelliteTunerAsync();
        string read = await SavedHashAsync(feature);

        Assert.Equal(HttpStatusCode.OK, await SaveAsync(feature, read, skyDisabled: true));
        Assert.False(Written(feature.Driver).Single(device => device.Id == Sky).Enabled);
    }

    [Fact]
    public async Task TheSecondOfTwoSavesMadeFromTheSameReadingIsRefusedAndLeavesTheFirstStanding()
    {
        await using AppSwapFeature feature = await WithASatelliteTunerAsync();
        string read = await SavedHashAsync(feature);

        Assert.Equal(HttpStatusCode.OK, await SaveAsync(feature, read, skyDisabled: true));

        string afterTheFirst = await File.ReadAllTextAsync(feature.Driver.LedgerPath);

        Assert.Equal(HttpStatusCode.Conflict, await SaveAsync(feature, read, skyDisabled: false));
        Assert.Equal(afterTheFirst, await File.ReadAllTextAsync(feature.Driver.LedgerPath));
    }

    [Fact]
    public async Task ASaveReadBeforeThePowerWasSwitchedCannotSwitchItBackUnseen()
    {
        await using AppSwapFeature feature = await WithASatelliteTunerAsync();
        string read = await SavedHashAsync(feature);

        using HttpResponseMessage switched = await feature.App.Client.PutAsJsonAsync(
            new Uri($"/api/tuners/{Sky}/lnb-power", UriKind.Relative),
            new { lnbPower = true });

        Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, await SaveAsync(feature, read, skyDisabled: false));
        Assert.True(Written(feature.Driver).Single(device => device.Id == Sky).LnbPower);
    }

    [Fact]
    public async Task TheRefusalSaysTheLedgerChangedSoTheScreenCanSaySo()
    {
        await using AppSwapFeature feature = await WithASatelliteTunerAsync();
        string read = await SavedHashAsync(feature);

        await SaveAsync(feature, read, skyDisabled: true);

        using HttpResponseMessage refused = await PutAsync(feature, read, skyDisabled: false);
        using JsonDocument body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());

        Assert.StartsWith("ledgerChanged", body.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
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

        using StringWriter refusal = new();

        Assert.Equal(0, await driver.RaiseFromTheLedgerAsync(refusal));

        await feature.App.UntilConnectedAsync();

        return feature;
    }

    private static IReadOnlyList<DeviceSettings> Written(SyntheticDriverHost driver)
    {
        DriverConfiguration? written = DriverConfigurationReader.Parse(File.ReadAllText(driver.LedgerPath));

        Assert.NotNull(written);

        return written.Devices ?? [];
    }

    private static async Task<string> SavedHashAsync(AppSwapFeature feature)
    {
        using HttpResponseMessage response = await feature.App.Client.GetAsync(Tuners);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("data").GetProperty("savedHash").GetString()!;
    }

    private static async Task<HttpStatusCode> SaveAsync(AppSwapFeature feature, string savedHash, bool skyDisabled)
    {
        using HttpResponseMessage response = await PutAsync(feature, savedHash, skyDisabled);

        return response.StatusCode;
    }

    private static Task<HttpResponseMessage> PutAsync(AppSwapFeature feature, string savedHash, bool skyDisabled)
        => feature.App.Client.PutAsJsonAsync(Tuners, new
        {
            savedHash,
            tuners = new[]
            {
                new { deviceId = Ground, disabled = false, lnbPower = false },
                new { deviceId = Sky, disabled = skyDisabled, lnbPower = false },
            },
        });
}
