using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Channels;
using Carina.Domain.Scans;

using Puppis.Contracts;

namespace Carina.Api.Tests.Feature.Scans;

[Trait("Category", "Feature")]
public sealed class StartScanActionTests
{
    private const int Terrestrial = 53;

    private const int SatelliteSlot = 5;

    [Fact]
    public async Task StartingAScanAnswersAtOnceWithTheIdentityToPollOn()
    {
        await using var feature = new ScanFeature { Orchestrator = { HoldsOpen = true } };

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/tuners/scan");

        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.NotEqual(Guid.Empty, body.GetProperty("data").GetProperty("scanId").GetGuid());
        Assert.True(feature.Runs.Runs[0].IsRunning);
    }

    [Fact]
    public async Task ASecondScanIsRefusedAndTellsTheClientWhichOneIsAlreadyWalking()
    {
        await using var feature = new ScanFeature { Orchestrator = { HoldsOpen = true } };

        Guid first = await feature.StartAsync();
        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/tuners/scan");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal(first, body.GetProperty("data").GetProperty("runningScanId").GetGuid());
    }

    [Fact]
    public async Task AScanThatCannotStartIsAnsweredAsUnavailableWithTheReason()
    {
        await using var feature = new ScanFeature
        {
            Orchestrator = { CouldNotStart = "the driver did not answer" },
        };

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/tuners/scan");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("the driver did not answer", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task AScanOfSystemsNoTunerInServiceReceivesIsRefusedAsUnprocessableWithTheReason()
    {
        await using var feature = new ScanFeature
        {
            Orchestrator = { NothingATunerCanReceive = "No tuner in service receives isdbSBs" },
        };

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/tuners/scan");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Equal("No tuner in service receives isdbSBs", body.GetProperty("message").GetString());
        Assert.Empty(feature.Runs.Runs);
    }

    [Fact]
    public async Task AScanOverNamedChannelsWalksExactlyThose()
    {
        await using var feature = new ScanFeature { Orchestrator = { HoldsOpen = true } };

        await feature.StartAsync(new
        {
            channels = new[] { new { system = "isdbT", physicalChannel = Terrestrial } },
        });

        Assert.Equal(
            [TuningParameters.Terrestrial(Terrestrial)],
            feature.Orchestrator.Scopes[0].NamedTargets);
    }

    [Fact]
    public async Task AScanOfOneSystemCarriesOnlyThatSystem()
    {
        await using var feature = new ScanFeature { Orchestrator = { HoldsOpen = true } };

        await feature.StartAsync(new { systems = new[] { "isdbT" } });

        Assert.Equal([TuneSystem.IsdbT], feature.Orchestrator.Scopes[0].Systems);
    }

    [Fact]
    public async Task AScanNamingNoSystemCoversEverything()
    {
        await using var feature = new ScanFeature { Orchestrator = { HoldsOpen = true } };

        await feature.StartAsync();

        Assert.Equal(ScanScope.Everything.Systems, feature.Orchestrator.Scopes[0].Systems);
    }

    [Fact]
    public async Task AChannelOutsideTheRangeTheStandardAllowsIsRefusedBeforeAnyTunerIsTouched()
    {
        await using var feature = new ScanFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/tuners/scan", new
        {
            channels = new[] { new { system = "isdbT", physicalChannel = 99 } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("13", body.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Empty(feature.Orchestrator.Scopes);
    }

    [Fact]
    public async Task ASatelliteSlotNamedWithoutItsStreamIsRefused()
    {
        await using var feature = new ScanFeature();

        (HttpStatusCode status, JsonElement _) = await feature.PostAsync("/api/tuners/scan", new
        {
            channels = new[] { new { system = "isdbSBs", physicalChannel = SatelliteSlot } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task EveryScanSurfaceIsBehindTheSameDenialAsTheRestOnceASchemeIsRegistered()
    {
        using var app = new TestingWebApplicationFactory();
        using HttpClient client = app.WithTestScheme().CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsync(new Uri("/api/tuners/scan", UriKind.Relative), null)).StatusCode);
    }
}
