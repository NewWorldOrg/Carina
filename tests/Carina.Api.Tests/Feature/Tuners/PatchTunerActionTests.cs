using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Reservations;

using Puppis.Contracts;

using static Carina.Api.Tests.Feature.Tuners.TunerLedgerFeature;

namespace Carina.Api.Tests.Feature.Tuners;

[Trait("Category", "Feature")]
public sealed class PatchTunerActionTests
{
    [Fact]
    public async Task ATunerTheDriverToggledAsksForTheAllocationToBeSettledAgain()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        using HttpResponseMessage response = await feature.Client.PatchAsJsonAsync(
            new Uri("/api/tuners/adapter0", UriKind.Relative),
            new { disabled = true });

        (HttpStatusCode status, JsonElement _) = await DriverFeature.ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal([RecalculationTrigger.TunerConfigurationChanged], feature.Notices.Nudged);
    }

    [Fact]
    public async Task ATunerTheDriverDoesNotHoldAsksForNothing()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        using HttpResponseMessage response = await feature.Client.PatchAsJsonAsync(
            new Uri("/api/tuners/adapter9", UriKind.Relative),
            new { disabled = true });

        (HttpStatusCode status, JsonElement _) = await DriverFeature.ReadAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Empty(feature.Notices.Nudged);
    }

    [Fact]
    public async Task TogglingATunerReachesTheDriverAndReportsWhatItAnswered()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        using HttpResponseMessage response = await feature.Client.PatchAsJsonAsync(
            new Uri("/api/tuners/adapter0", UriKind.Relative),
            new { disabled = true });

        (HttpStatusCode status, JsonElement body) = await DriverFeature.ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("adapter0", feature.Driver.LastToggledDeviceId);
        Assert.True(feature.Driver.LastToggle!.Disabled);
        Assert.Equal("adapter0", body.GetProperty("data").GetProperty("deviceId").GetString());
    }

    [Fact]
    public async Task ATunerBusyWhenDisabledIsReportedAsPendingRatherThanDone()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), driver =>
        {
            Stocked(driver);
            driver.Tuners =
            [
                new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Draining)
                {
                    Health = new TunerHealthDto { Level = TunerHealthLevel.Healthy, DisablePending = true },
                },
            ];
        });

        using HttpResponseMessage response = await feature.Client.PatchAsJsonAsync(
            new Uri("/api/tuners/adapter0", UriKind.Relative),
            new { disabled = true });

        (HttpStatusCode _, JsonElement body) = await DriverFeature.ReadAsync(response);
        JsonElement data = body.GetProperty("data");

        Assert.Equal("draining", data.GetProperty("state").GetString());
        Assert.True(data.GetProperty("disablePending").GetBoolean());
    }

    [Fact]
    public async Task TogglingATunerTheDriverDoesNotHoldIsAnsweredAsNotFound()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        using HttpResponseMessage response = await feature.Client.PatchAsJsonAsync(
            new Uri("/api/tuners/adapter9", UriKind.Relative),
            new { disabled = true });

        (HttpStatusCode status, JsonElement _) = await DriverFeature.ReadAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task ADriverThatCannotToggleWhileRunningSaysSoWithoutFailingTheScreen()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(
            Capable([DriverCapabilities.TunerLedger]),
            Stocked);

        using HttpResponseMessage response = await feature.Client.PatchAsJsonAsync(
            new Uri("/api/tuners/adapter0", UriKind.Relative),
            new { disabled = true });

        (HttpStatusCode status, JsonElement _) = await DriverFeature.ReadAsync(response);

        Assert.Equal(HttpStatusCode.NotImplemented, status);
    }

    [Fact]
    public async Task AToggleWithoutASideIsRefusedBeforeTheDriverIsAsked()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(Capable(), Stocked);

        using HttpResponseMessage response = await feature.Client.PatchAsJsonAsync(
            new Uri("/api/tuners/adapter0", UriKind.Relative),
            new { });

        (HttpStatusCode status, JsonElement _) = await DriverFeature.ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Null(feature.Driver.LastToggledDeviceId);
    }
}
