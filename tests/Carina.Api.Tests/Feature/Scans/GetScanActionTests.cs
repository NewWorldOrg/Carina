using System.Net;
using System.Text.Json;

using Carina.Api.Common;
using Carina.Domain.Channels;
using Carina.Domain.Scans;
using Carina.TestSupport;

namespace Carina.Api.Tests.Feature.Scans;

[Trait("Category", "Feature")]
public sealed class GetScanActionTests
{
    private const int Terrestrial = 53;

    private const int OtherTerrestrial = 55;

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

    [Fact]
    public async Task ProgressTellsTheFourWaysOfFailingApartAndCarriesAReasonForEachOne()
    {
        await using var feature = new ScanFeature
        {
            Orchestrator =
            {
                Walked =
                {
                    TuningParameters.Terrestrial(Terrestrial),
                    TuningParameters.Terrestrial(OtherTerrestrial),
                    TuningParameters.Terrestrial(Terrestrial),
                    TuningParameters.Terrestrial(OtherTerrestrial),
                },
                Outcomes =
                {
                    ScanAttemptOutcome.NoLock,
                    ScanAttemptOutcome.LockedWithoutData,
                    ScanAttemptOutcome.IncompleteTables,
                    ScanAttemptOutcome.UnexpectedStream,
                },
            },
        };

        Guid scanId = await feature.StartAsync();

        await feature.UntilSettled(scanId);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/tuners/scan/{scanId}");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(4, data.GetProperty("attempted").GetInt32());
        Assert.Equal(0, data.GetProperty("succeeded").GetInt32());
        Assert.Equal(
            ["noLock", "lockedWithoutData", "incompleteTables", "unexpectedStream"],
            data.GetProperty("attempts").EnumerateArray()
                .Select(attempt => attempt.GetProperty("outcome").GetString()));
        Assert.Equal(
            [
                "scripted NoLock",
                "scripted LockedWithoutData",
                "scripted IncompleteTables",
                "scripted UnexpectedStream",
            ],
            data.GetProperty("attempts").EnumerateArray()
                .Select(attempt => attempt.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task ProgressReportsWhatHasBeenWalkedSoFar()
    {
        await using var feature = new ScanFeature
        {
            Orchestrator = { HoldsOpen = true, Walked = { TuningParameters.Terrestrial(Terrestrial) } },
        };

        Guid scanId = await feature.StartAsync();

        await Eventually.Happens(
            () => feature.Runs.Attempts.Count == 1,
            "the walk records its first attempt");

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/tuners/scan/{scanId}");
        JsonElement data = body.GetProperty("data");

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

        (HttpStatusCode status, JsonElement _) = await feature.GetAsync($"/api/tuners/scan/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task AScanNamedByTheUuidThatIsAllZeroesIsRefusedAsARequestAndNotAFault()
    {
        await using var feature = new ScanFeature();
        string path = $"/api/tuners/scan/{Guid.Empty}";

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(ScanIdText.Description, body.GetProperty("message").GetString());
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

        Guid scanId = await feature.StartAsync();
        await feature.UntilSettled(scanId);

        (HttpStatusCode _, JsonElement body) = await feature.GetAsync($"/api/tuners/scan/{scanId}");
        JsonElement difference = body.GetProperty("data").GetProperty("difference");

        Assert.Equal(1, difference.GetProperty("added").GetArrayLength());
        Assert.Equal("Arrived", difference.GetProperty("added")[0].GetProperty("name").GetString());
        Assert.Equal(101, difference.GetProperty("added")[0].GetProperty("serviceId").GetInt32());
    }
}
