extern alias driver;

using System.Net;
using System.Runtime.Versioning;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Channels;
using Carina.Domain.Reservations;

using driver::Puppis.Driver.Configuration;

using Puppis.Contracts;

using static Carina.Api.Tests.Feature.Tuners.SyntheticTunerLedger;
using static Carina.Api.Tests.Feature.Tuners.TunerLedgerFeature;

namespace Carina.Api.Tests.Feature.Tuners;

[SupportedOSPlatform("linux")]
[Trait("Category", "Feature")]
public sealed class PutTunersActionTests
{
    [Fact]
    public async Task ALedgerThatWasSavedAsksForTheAllocationToBeSettledAgain()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Unconfigured);

        using HttpResponseMessage saving = await feature.Client.PutAsJsonAsync(TunersPath, new
        {
            tuners = new[] { new { deviceId = "adapter0", disabled = false, lnbPower = false } },
        });

        (HttpStatusCode status, JsonElement _) = await DriverFeature.ReadAsync(saving);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal([RecalculationTrigger.TunerConfigurationChanged], feature.Notices.Nudged);
    }

    [Fact]
    public async Task ALedgerThatWasRefusedAsksForNothing()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Unconfigured);

        using HttpResponseMessage saving = await feature.Client.PutAsJsonAsync(
            TunersPath,
            new { tuners = Array.Empty<object>() });

        (HttpStatusCode status, JsonElement _) = await DriverFeature.ReadAsync(saving);

        Assert.NotEqual(HttpStatusCode.OK, status);
        Assert.Empty(feature.Notices.Nudged);
    }

    [Fact]
    public async Task AFreshlySavedLedgerSaysItIsAheadOfWhatTheDriverIsRunning()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Unconfigured);

        using HttpResponseMessage saving = await feature.Client.PutAsJsonAsync(TunersPath, new
        {
            tuners = new[] { new { deviceId = "adapter0", disabled = false, lnbPower = false } },
        });

        (HttpStatusCode _, JsonElement body) = await DriverFeature.ReadAsync(saving);
        JsonElement data = body.GetProperty("data");

        Assert.Equal(
            ["adapter0"],
            data.GetProperty("desired").EnumerateArray()
                .Select(entry => entry.GetProperty("deviceId").GetString()));
        Assert.True(data.GetProperty("drifted").GetBoolean());
    }

    [Fact]
    public async Task SavingALedgerHandsTheDriverOnlyTheDeviceIdsItDetected()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        using HttpResponseMessage response = await feature.Client.PutAsJsonAsync(TunersPath, new
        {
            tuners = new[] { new { deviceId = "adapter0", disabled = false, lnbPower = true } },
        });

        (HttpStatusCode status, JsonElement _) = await DriverFeature.ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            ["adapter0"],
            feature.Driver.LastReplacedLedger!.Select(entry => entry.DeviceId));
        Assert.True(feature.Driver.LastReplacedLedger![0].LnbPower);
    }

    [Fact]
    public async Task SavingALedgerAsksEveryCandidateToProveItselfAgain()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        Seed(feature, 53, 57);

        using HttpResponseMessage response = await feature.Client.PutAsJsonAsync(TunersPath, new
        {
            tuners = new[] { new { deviceId = "adapter0" } },
        });

        (HttpStatusCode status, JsonElement _) = await DriverFeature.ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(2, feature.Candidates.Candidates.Count);
        Assert.All(feature.Candidates.Candidates, candidate => Assert.True(candidate.NeedsRevalidation));
    }

    [Fact]
    public async Task ALedgerRefusedBeforeTheDriverIsAskedLeavesTheCandidatesAlone()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        Seed(feature, 53);

        using HttpResponseMessage response = await feature.Client.PutAsJsonAsync(
            TunersPath,
            new { tuners = Array.Empty<object>() });

        (HttpStatusCode status, JsonElement _) = await DriverFeature.ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Single(feature.Candidates.Candidates);
        Assert.All(feature.Candidates.Candidates, candidate => Assert.False(candidate.NeedsRevalidation));
    }

    [Fact]
    public async Task ALedgerTheDriverRefusesLeavesTheCandidatesAlone()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(
            Capable([DriverCapabilities.DeviceDetection]),
            Stocked);

        Seed(feature, 53);

        using HttpResponseMessage response = await feature.Client.PutAsJsonAsync(TunersPath, new
        {
            tuners = new[] { new { deviceId = "adapter0" } },
        });

        (HttpStatusCode status, JsonElement _) = await DriverFeature.ReadAsync(response);

        Assert.NotEqual(HttpStatusCode.OK, status);
        Assert.Single(feature.Candidates.Candidates);
        Assert.All(feature.Candidates.Candidates, candidate => Assert.False(candidate.NeedsRevalidation));
    }

    [Fact]
    public async Task AnEmptyLedgerIsRefusedBeforeTheDriverIsEvenAsked()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        using HttpResponseMessage response = await feature.Client.PutAsJsonAsync(TunersPath, new { tuners = Array.Empty<object>() });
        (HttpStatusCode status, JsonElement _) = await DriverFeature.ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Null(feature.Driver.LastReplacedLedger);
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("types")]
    [InlineData("devicePath")]
    public async Task ALedgerThatTriesToSayWhatATunerIsOrWhereItSitsIsRefusedRatherThanSavedWithoutIt(string field)
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        using var sent = new StringContent(
            $$"""{"tuners":[{"deviceId":"adapter0","disabled":false,"lnbPower":false,"{{field}}":"bs"}]}""",
            System.Text.Encoding.UTF8,
            "application/json");
        using HttpResponseMessage response = await feature.Client.PutAsync(TunersPath, sent);

        (HttpStatusCode status, JsonElement body) = await DriverFeature.ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.False(body.GetProperty("status").GetBoolean());
        Assert.Null(feature.Driver.LastReplacedLedger);
    }

    [Fact]
    public async Task ALedgerNamingSomethingThatIsNotADeviceIdIsRefused()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        using HttpResponseMessage response = await feature.Client.PutAsJsonAsync(TunersPath, new
        {
            tuners = new[] { new { deviceId = "../../dev/dvb/adapter0/frontend0" } },
        });

        (HttpStatusCode status, JsonElement _) = await DriverFeature.ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Null(feature.Driver.LastReplacedLedger);
    }

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

    [Fact]
    public async Task AMachineWithNothingConfiguredIsSetUpThroughTheApiAndNowhereElse()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Unconfigured);

        using HttpResponseMessage saving = await feature.Client.PutAsJsonAsync(TunersPath, new
        {
            tuners = new[]
            {
                new { deviceId = "adapter0", disabled = false, lnbPower = false },
                new { deviceId = "adapter1", disabled = false, lnbPower = false },
            },
        });

        (HttpStatusCode saved, JsonElement _) = await DriverFeature.ReadAsync(saving);

        Assert.Equal(HttpStatusCode.OK, saved);

        (HttpStatusCode listed, JsonElement ledger) = await DriverFeature.ReadAsync(await feature.Client.GetAsync(TunersPath));

        Assert.Equal(HttpStatusCode.OK, listed);
        Assert.Equal(
            ["adapter0", "adapter1"],
            ledger.GetProperty("data").GetProperty("desired").EnumerateArray()
                .Select(entry => entry.GetProperty("deviceId").GetString()));
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

    private static async Task<string> SavedHashAsync(AppSwapFeature feature)
    {
        using HttpResponseMessage response = await feature.App.Client.GetAsync(TunersPath);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("data").GetProperty("savedHash").GetString()!;
    }

    private static async Task<HttpStatusCode> SaveAsync(AppSwapFeature feature, string savedHash, bool skyDisabled)
    {
        using HttpResponseMessage response = await PutAsync(feature, savedHash, skyDisabled);

        return response.StatusCode;
    }

    private static Task<HttpResponseMessage> PutAsync(AppSwapFeature feature, string savedHash, bool skyDisabled)
        => feature.App.Client.PutAsJsonAsync(TunersPath, new
        {
            savedHash,
            tuners = new[]
            {
                new { deviceId = Ground, disabled = false, lnbPower = false },
                new { deviceId = Sky, disabled = skyDisabled, lnbPower = false },
            },
        });

    private static void Seed(DriverFeature feature, params int[] physicalChannels)
    {
        foreach (int channel in physicalChannels)
        {
            feature.Candidates.Candidates.Add(CandidateChannel.Discover(
                CandidateChannelId.New(),
                new NetworkId(1),
                new ServiceId(1024 + channel),
                TuningParameters.Terrestrial(channel),
                Started.UtcDateTime));
        }
    }
}
