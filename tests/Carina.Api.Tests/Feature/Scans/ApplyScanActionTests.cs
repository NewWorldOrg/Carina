using System.Net;
using System.Text.Json;

using Carina.Api.Common;
using Carina.Domain.Channels;
using Carina.Domain.Scans;

namespace Carina.Api.Tests.Feature.Scans;

[Trait("Category", "Feature")]
public sealed class ApplyScanActionTests
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
    public async Task AScanNamedByTheUuidThatIsAllZeroesIsRefusedAsARequestAndNotAFault()
    {
        await using var feature = new ScanFeature();
        string path = $"/api/tuners/scan/{Guid.Empty}/apply";

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(ScanIdText.Description, body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ApplyingWhileTheScanIsStillWalkingIsRefused()
    {
        await using var feature = new ScanFeature { Orchestrator = { HoldsOpen = true } };

        Guid scanId = await feature.StartAsync();
        (HttpStatusCode status, JsonElement _) = await feature.PostAsync($"/api/tuners/scan/{scanId}/apply");

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

        Guid scanId = await feature.StartAsync();
        await feature.PostAsync($"/api/tuners/scan/{scanId}/cancel");
        await feature.UntilSettled(scanId);

        (HttpStatusCode status, JsonElement _) = await feature.PostAsync($"/api/tuners/scan/{scanId}/apply");

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

        Guid scanId = await feature.StartAsync();
        await feature.UntilSettled(scanId);

        Assert.Empty(feature.Services.Services);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync($"/api/tuners/scan/{scanId}/apply");

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

        Guid scanId = await feature.StartAsync(new { systems = new[] { "isdbT" } });
        await feature.UntilSettled(scanId);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync($"/api/tuners/scan/{scanId}/apply");

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

        Guid scanId = await feature.StartAsync(new { systems = new[] { "isdbT" } });
        await feature.UntilSettled(scanId);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync($"/api/tuners/scan/{scanId}/apply");

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

        Guid scanId = await feature.StartAsync();
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

        Guid scanId = await feature.StartAsync();
        await feature.UntilSettled(scanId);

        (HttpStatusCode refused, JsonElement complaint) = await feature.PostAsync($"/api/tuners/scan/{scanId}/apply");

        Assert.Equal(HttpStatusCode.InternalServerError, refused);
        Assert.False(complaint.GetProperty("status").GetBoolean());
        Assert.Contains(
            UnhandledFailureMiddleware.Message,
            complaint.GetProperty("message").GetString()!,
            StringComparison.Ordinal);

        feature.WhenACandidateArrives = () => false;

        (HttpStatusCode status, JsonElement _) = await feature.PostAsync($"/api/tuners/scan/{scanId}/apply");

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

        Guid scanId = await feature.StartAsync();
        await feature.UntilSettled(scanId);

        var reached = new TaskCompletionSource();
        var letGo = new TaskCompletionSource();
        feature.WhenACandidateArrives = () =>
        {
            reached.TrySetResult();
            letGo.Task.Wait();

            return false;
        };

        Task<(HttpStatusCode Status, JsonElement Body)> held = feature.PostAsync($"/api/tuners/scan/{scanId}/apply");
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

        (HttpStatusCode status, JsonElement _) = await feature.PostAsync($"/api/tuners/scan/{Guid.NewGuid()}/apply");

        Assert.Equal(HttpStatusCode.NotFound, status);
    }
}
