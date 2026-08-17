using System.Net;
using System.Text.Json;

using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Scans;
using Carina.TestSupport;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class ScanEndpointTests
{
    private const int Terrestrial = 53;
    private const int OtherTerrestrial = 55;
    private const int SatelliteSlot = 5;
    private const int SatelliteStream = 50001;

    private static readonly DateTime At = new(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);

    private static ScanDifference Proposing(params ScanServiceChange[] changes) => new(changes, []);

    private static ScanServiceChange Arriving(int serviceId, string name, TuningParameters tuning)
        => new(
            ScanChangeKind.Added,
            new NetworkId(1),
            new ServiceId(serviceId),
            name,
            ServiceCategory.Television,
            [new ScanChannelChange(ScanChangeKind.Added, tuning, tuning.TransportStreamId, null)],
            Seen: true);

    private static ScanServiceChange Leaving(int serviceId, string name, TuningParameters tuning)
        => new(
            ScanChangeKind.Missing,
            new NetworkId(1),
            new ServiceId(serviceId),
            name,
            ServiceCategory.Television,
            [new ScanChannelChange(ScanChangeKind.Missing, tuning, tuning.TransportStreamId, null)],
            Seen: false);

    private static TuningParameters Satellite()
        => TuningParameters.Bs(SatelliteSlot, new TransportStreamId(SatelliteStream));

    [Fact]
    public async Task StartingAScanAnswersAtOnceWithTheIdentityToPollOn()
    {
        await using var feature = new ScanFeature { Orchestrator = { HoldsOpen = true } };

        var (status, body) = await feature.PostAsync("/api/tuners/scan");

        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.NotEqual(Guid.Empty, body.GetProperty("data").GetProperty("scanId").GetGuid());
        Assert.True(feature.Runs.Runs[0].IsRunning);
    }

    [Fact]
    public async Task ASecondScanIsRefusedAndTellsTheClientWhichOneIsAlreadyWalking()
    {
        await using var feature = new ScanFeature { Orchestrator = { HoldsOpen = true } };

        var first = await feature.StartAsync();
        var (status, body) = await feature.PostAsync("/api/tuners/scan");

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

        var (status, body) = await feature.PostAsync("/api/tuners/scan");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("the driver did not answer", body.GetProperty("message").GetString());
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

        var (status, body) = await feature.PostAsync("/api/tuners/scan", new
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

        var (status, _) = await feature.PostAsync("/api/tuners/scan", new
        {
            channels = new[] { new { system = "isdbSBs", physicalChannel = SatelliteSlot } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task ProgressReportsWhatHasBeenWalkedSoFar()
    {
        await using var feature = new ScanFeature
        {
            Orchestrator = { HoldsOpen = true, Walked = { TuningParameters.Terrestrial(Terrestrial) } },
        };

        var scanId = await feature.StartAsync();

        await Eventually.Happens(
            () => feature.Runs.Attempts.Count == 1,
            "the walk records its first attempt");

        var (status, body) = await feature.GetAsync($"/api/tuners/scan/{scanId}");
        var data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("running", data.GetProperty("run").GetProperty("state").GetString());
        Assert.Equal(1, data.GetProperty("attempted").GetInt32());
        Assert.Equal(1, data.GetProperty("succeeded").GetInt32());
        Assert.Equal(
            Terrestrial,
            data.GetProperty("attempts")[0].GetProperty("target").GetProperty("physicalChannel").GetInt32());
    }

    [Fact]
    public async Task ProgressOfAScanNobodyStartedIsAnsweredAsNotFound()
    {
        await using var feature = new ScanFeature();

        var (status, _) = await feature.GetAsync($"/api/tuners/scan/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task ACompletedScanShowsTheDifferenceItIsProposing()
    {
        await using var feature = new ScanFeature
        {
            Orchestrator =
            {
                Difference = Proposing(
                    Arriving(101, "Arrived", TuningParameters.Terrestrial(Terrestrial))),
            },
        };

        var scanId = await feature.StartAsync();
        await feature.UntilSettled(scanId);

        var (_, body) = await feature.GetAsync($"/api/tuners/scan/{scanId}");
        var difference = body.GetProperty("data").GetProperty("difference");

        Assert.Equal(1, difference.GetProperty("added").GetArrayLength());
        Assert.Equal("Arrived", difference.GetProperty("added")[0].GetProperty("name").GetString());
        Assert.Equal(101, difference.GetProperty("added")[0].GetProperty("serviceId").GetInt32());
    }

    [Fact]
    public async Task CancellingAWalkingScanLeavesTheExistingDefinitionsAlone()
    {
        await using var feature = new ScanFeature { Orchestrator = { HoldsOpen = true } };

        feature.Services.Services.Add(BroadcastService.Discover(
            new NetworkId(1),
            new ServiceId(101),
            "Untouched",
            ServiceCategory.Television,
            At));

        var scanId = await feature.StartAsync();
        var (status, _) = await feature.PostAsync($"/api/tuners/scan/{scanId}/cancel");

        await feature.UntilSettled(scanId);

        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.Equal(ScanRunState.Cancelled, feature.Runs.Runs[0].State);
        Assert.Single(feature.Services.Services);
    }

    [Fact]
    public async Task CancellingAScanThatHasAlreadyEndedIsRefused()
    {
        await using var feature = new ScanFeature();

        var scanId = await feature.StartAsync();
        await feature.UntilSettled(scanId);

        var (status, _) = await feature.PostAsync($"/api/tuners/scan/{scanId}/cancel");

        Assert.Equal(HttpStatusCode.Conflict, status);
    }

    [Fact]
    public async Task ApplyingWhileTheScanIsStillWalkingIsRefused()
    {
        await using var feature = new ScanFeature { Orchestrator = { HoldsOpen = true } };

        var scanId = await feature.StartAsync();
        var (status, _) = await feature.PostAsync($"/api/tuners/scan/{scanId}/apply");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Empty(feature.Services.Services);
    }

    [Fact]
    public async Task ACancelledScanIsNeverApplied()
    {
        await using var feature = new ScanFeature
        {
            Orchestrator =
            {
                HoldsOpen = true,
                Difference = Proposing(
                    Arriving(101, "Arrived", TuningParameters.Terrestrial(Terrestrial))),
            },
        };

        var scanId = await feature.StartAsync();
        await feature.PostAsync($"/api/tuners/scan/{scanId}/cancel");
        await feature.UntilSettled(scanId);

        var (status, _) = await feature.PostAsync($"/api/tuners/scan/{scanId}/apply");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Empty(feature.Services.Services);
    }

    [Fact]
    public async Task ApplyingWritesTheProposedDifferenceOnlyWhenItIsAsked()
    {
        await using var feature = new ScanFeature
        {
            Orchestrator =
            {
                Difference = Proposing(
                    Arriving(101, "Arrived", TuningParameters.Terrestrial(Terrestrial))),
            },
        };

        var scanId = await feature.StartAsync();
        await feature.UntilSettled(scanId);

        Assert.Empty(feature.Services.Services);

        var (status, body) = await feature.PostAsync($"/api/tuners/scan/{scanId}/apply");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, body.GetProperty("data").GetProperty("servicesAdded").GetInt32());
        Assert.Single(feature.Services.Services);
    }

    [Fact]
    public async Task ApplyingATerrestrialScanDoesNotRemoveSatelliteServices()
    {
        await using var feature = new ScanFeature
        {
            Orchestrator =
            {
                Walked = { TuningParameters.Terrestrial(Terrestrial) },
                Difference = Proposing(
                    Arriving(101, "Arrived", TuningParameters.Terrestrial(OtherTerrestrial)),
                    Leaving(201, "Satellite one", Satellite())),
            },
        };

        feature.Services.Services.Add(BroadcastService.Discover(
            new NetworkId(1),
            new ServiceId(201),
            "Satellite one",
            ServiceCategory.Television,
            At));
        feature.Candidates.Candidates.Add(CandidateChannel.Discover(
            CandidateChannelId.New(),
            new NetworkId(1),
            new ServiceId(201),
            Satellite(),
            At));

        var scanId = await feature.StartAsync(new { systems = new[] { "isdbT" } });
        await feature.UntilSettled(scanId);

        var (status, body) = await feature.PostAsync($"/api/tuners/scan/{scanId}/apply");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            ["isdbT"],
            body.GetProperty("data").GetProperty("systems").EnumerateArray()
                .Select(system => system.GetString()));
        Assert.Equal(0, body.GetProperty("data").GetProperty("servicesRemoved").GetInt32());
        Assert.Contains(feature.Services.Services, service => service.ServiceId.Value == 201);
        Assert.Contains(feature.Candidates.Candidates, candidate => candidate.Tuning.Equals(Satellite()));
    }

    [Fact]
    public async Task AScanWhoseEveryAttemptFailedStillAppliesTheRemovalsItProposed()
    {
        await using var feature = new ScanFeature
        {
            Orchestrator =
            {
                Walked = { TuningParameters.Terrestrial(Terrestrial) },
                EveryAttemptFails = true,
                Difference = Proposing(
                    Leaving(101, "Gone dark", TuningParameters.Terrestrial(Terrestrial))),
            },
        };

        feature.Services.Services.Add(BroadcastService.Discover(
            new NetworkId(1),
            new ServiceId(101),
            "Gone dark",
            ServiceCategory.Television,
            At));
        feature.Candidates.Candidates.Add(CandidateChannel.Discover(
            CandidateChannelId.New(),
            new NetworkId(1),
            new ServiceId(101),
            TuningParameters.Terrestrial(Terrestrial),
            At));

        var scanId = await feature.StartAsync(new { systems = new[] { "isdbT" } });
        await feature.UntilSettled(scanId);

        var (status, body) = await feature.PostAsync($"/api/tuners/scan/{scanId}/apply");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, body.GetProperty("data").GetProperty("servicesRemoved").GetInt32());
        Assert.Empty(feature.Services.Services);
    }

    [Fact]
    public async Task ADifferenceIsAppliedOnceAndTheSecondAskIsRefused()
    {
        await using var feature = new ScanFeature
        {
            Orchestrator =
            {
                Difference = Proposing(
                    Arriving(101, "Arrived", TuningParameters.Terrestrial(Terrestrial))),
            },
        };

        var scanId = await feature.StartAsync();
        await feature.UntilSettled(scanId);

        Assert.Equal(HttpStatusCode.OK, (await feature.PostAsync($"/api/tuners/scan/{scanId}/apply")).Status);
        Assert.Equal(
            HttpStatusCode.Gone,
            (await feature.PostAsync($"/api/tuners/scan/{scanId}/apply")).Status);
        Assert.Single(feature.Services.Services);
    }

    [Fact]
    public async Task AnApplyThatDidNotLandLeavesTheDifferenceStillApplicable()
    {
        await using var feature = new ScanFeature
        {
            Orchestrator =
            {
                Walked = { TuningParameters.Terrestrial(Terrestrial) },
                Difference = Proposing(
                    Arriving(101, "Arrived", TuningParameters.Terrestrial(Terrestrial))),
            },
            WhenACandidateArrives = () => true,
        };

        var scanId = await feature.StartAsync();
        await feature.UntilSettled(scanId);

        var (refused, _) = await feature.PostAsync($"/api/tuners/scan/{scanId}/apply");

        Assert.Equal(HttpStatusCode.InternalServerError, refused);

        feature.WhenACandidateArrives = () => false;

        var (status, _) = await feature.PostAsync($"/api/tuners/scan/{scanId}/apply");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Single(feature.Candidates.Candidates);
    }

    [Fact]
    public async Task ApplyingWhileAnotherApplyHoldsTheDifferenceSaysToWaitRatherThanToWalkAgain()
    {
        await using var feature = new ScanFeature
        {
            Orchestrator =
            {
                Walked = { TuningParameters.Terrestrial(Terrestrial) },
                Difference = Proposing(
                    Arriving(101, "Arrived", TuningParameters.Terrestrial(Terrestrial))),
            },
        };

        var scanId = await feature.StartAsync();
        await feature.UntilSettled(scanId);

        var reached = new TaskCompletionSource();
        var letGo = new TaskCompletionSource();
        feature.WhenACandidateArrives = () =>
        {
            reached.TrySetResult();
            letGo.Task.Wait();

            return false;
        };

        var held = feature.PostAsync($"/api/tuners/scan/{scanId}/apply");
        HttpStatusCode status;
        JsonElement body;

        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            (status, body) = await feature.PostAsync($"/api/tuners/scan/{scanId}/apply");
        }
        finally
        {
            letGo.SetResult();
        }

        Assert.Equal(HttpStatusCode.OK, (await held).Status);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains(
            "being applied",
            body.GetProperty("message").GetString()!,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyingAScanNobodyStartedIsAnsweredAsNotFound()
    {
        await using var feature = new ScanFeature();

        var (status, _) = await feature.PostAsync($"/api/tuners/scan/{Guid.NewGuid()}/apply");

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task PastRunsAreListedNewestFirstWithHowTheyEnded()
    {
        await using var feature = new ScanFeature();

        var first = await feature.StartAsync();
        await feature.UntilSettled(first);

        var (status, body) = await feature.GetAsync("/api/tuners/scan-runs");
        var runs = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, runs.GetArrayLength());
        Assert.Equal(first, runs[0].GetProperty("scanId").GetGuid());
        Assert.Equal("completed", runs[0].GetProperty("state").GetString());
    }

    [Fact]
    public async Task EveryScanSurfaceIsBehindTheSameDenialAsTheRestOnceASchemeIsRegistered()
    {
        using var app = new TestingWebApplicationFactory();
        using var client = app.WithTestScheme().CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync(new Uri("/api/tuners/scan-runs", UriKind.Relative))).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsync(new Uri("/api/tuners/scan", UriKind.Relative), null)).StatusCode);
    }
}
