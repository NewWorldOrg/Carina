using System.Net;
using System.Text.Json;

using Carina.Domain.Migration;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class MigrationRecordEndpointTests
{
    private const string Record = "/api/migration/record";

    [Fact]
    public async Task NoMigrationHasEverRunIsAnsweredAsAnEmptyRecordRatherThanARefusal()
    {
        await using var feature = new MigrationFeature();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(Record);
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("run").ValueKind);
        Assert.Empty(data.GetProperty("populations").EnumerateArray());
        Assert.Empty(data.GetProperty("losses").EnumerateArray());
        Assert.Empty(data.GetProperty("items").EnumerateArray());
        Assert.Equal(0, data.GetProperty("total").GetInt32());
        Assert.Equal(1, data.GetProperty("lastPage").GetInt32());
    }

    [Fact]
    public async Task TheRunComesBackWithWhatItWasAndHowManyRehearsalsStandBehindIt()
    {
        await using var feature = new MigrationFeature();
        feature.Records.Kept = MigrationFeature.Carried(
            MigrationPass.ForReal,
            MigrationVerdict.Carry(MigrationPopulation.Recordings, "1", "a programme", 100, 100));
        feature.Records.Rehearsals = 4;
        feature.Records.LastRehearsalFinishedAt = MigrationFeature.Small.AddHours(-5);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(Record);
        JsonElement run = body.GetProperty("data").GetProperty("run");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(feature.Records.Kept.Run.Id.Value, run.GetProperty("id").GetGuid());
        Assert.Equal("forReal", run.GetProperty("pass").GetString());
        Assert.Equal(MigrationFeature.Source.Value, run.GetProperty("source").GetString());
        Assert.Equal(MigrationFeature.Small, run.GetProperty("startedAt").GetDateTime());
        Assert.Equal(MigrationFeature.Small.AddMinutes(6), run.GetProperty("finishedAt").GetDateTime());
        Assert.Equal(4, run.GetProperty("rehearsals").GetInt32());
        Assert.Equal(
            MigrationFeature.Small.AddHours(-5),
            run.GetProperty("lastRehearsalFinishedAt").GetDateTime());
    }

    [Fact]
    public async Task ThePopulationsComeBackWithWhatWasCarriedWhatWasNotAndNothingLeftUnclassed()
    {
        await using var feature = new MigrationFeature();
        feature.Records.Kept = MigrationFeature.Carried(
            MigrationPass.ForReal,
            MigrationVerdict.Carry(MigrationPopulation.Recordings, "1", "a programme", 100, 100),
            MigrationVerdict.Refuse(
                MigrationPopulation.RecordingFiles,
                "stray.sh",
                MigrationRefusal.Orphan,
                "stray.sh",
                null,
                1_024));

        (_, JsonElement body) = await feature.GetAsync(Record);
        JsonElement data = body.GetProperty("data");
        JsonElement[] populations = [.. data.GetProperty("populations").EnumerateArray()];

        Assert.Equal(MigrationPopulations.Counted.Count, populations.Length);
        Assert.Equal(0, data.GetProperty("unclassified").GetInt32());

        JsonElement files = populations.Single(
            population => population.GetProperty("population").GetString() == "recordingFiles");

        Assert.Equal(1, files.GetProperty("offered").GetInt32());
        Assert.Equal(0, files.GetProperty("carried").GetInt32());
        Assert.Equal(1, files.GetProperty("notCarried").GetInt32());
        Assert.Equal(0, files.GetProperty("unclassified").GetInt32());
    }

    [Fact]
    public async Task EveryReasonForLeavingSomethingBehindIsAnsweredEvenWhereNothingWasLeftForIt()
    {
        await using var feature = new MigrationFeature();
        feature.Records.Kept = MigrationFeature.Carried(
            MigrationPass.ForReal,
            MigrationVerdict.Refuse(
                MigrationPopulation.RecordingFiles,
                "stray.sh",
                MigrationRefusal.Orphan,
                "stray.sh",
                null,
                1_024));

        (_, JsonElement body) = await feature.GetAsync(Record);
        JsonElement[] refusals = [.. body.GetProperty("data").GetProperty("refusals").EnumerateArray()];

        Assert.Equal(MigrationRefusals.All.Count, refusals.Length);
        Assert.Equal(
            1,
            refusals.Single(one => one.GetProperty("refusal").GetString() == "orphan")
                .GetProperty("count")
                .GetInt32());
        Assert.Equal(
            0,
            refusals.Single(one => one.GetProperty("refusal").GetString() == "noSuchFeature")
                .GetProperty("count")
                .GetInt32());
    }

    [Fact]
    public async Task WhatWasCarriedAndArrivedDiminishedComesBackAsOneLineEachWithHowMuchItReaches()
    {
        await using var feature = new MigrationFeature();
        feature.Records.Kept = MigrationFeature.Carried(MigrationPass.ForReal);

        (_, JsonElement body) = await feature.GetAsync(Record);
        JsonElement[] losses = [.. body.GetProperty("data").GetProperty("losses").EnumerateArray()];

        Assert.Equal(
            ["dayBoundary", "duplicateAvoidance", "enclosedCharacters"],
            losses.Select(one => one.GetProperty("subject").GetString()).Order(StringComparer.Ordinal));
        Assert.All(
            losses,
            one => Assert.Equal(JsonValueKind.Number, one.GetProperty("affected").ValueKind));
    }

    [Fact]
    public async Task ALineOfWhatWasNotCarriedKeepsItsReasonItsSubjectAndBothSizesApart()
    {
        await using var feature = new MigrationFeature();
        feature.Records.Kept = MigrationFeature.Carried(
            MigrationPass.ForReal,
            MigrationVerdict.Refuse(
                MigrationPopulation.Recordings,
                "7",
                MigrationRefusal.ReallyEmpty,
                "a programme",
                17_000_000_000,
                0));

        (_, JsonElement body) = await feature.GetAsync(Record);
        JsonElement row = Assert.Single(body.GetProperty("data").GetProperty("items").EnumerateArray());

        Assert.Equal("recordings", row.GetProperty("population").GetString());
        Assert.Equal("reallyEmpty", row.GetProperty("refusal").GetString());
        Assert.Equal("7", row.GetProperty("subject").GetString());
        Assert.Equal("a programme", row.GetProperty("note").GetString());
        Assert.Equal(17_000_000_000, row.GetProperty("claimed").GetInt64());
        Assert.Equal(0, row.GetProperty("observed").GetInt64());
    }

    [Fact]
    public async Task EveryLineIsReachedByWalkingThePagesAndTheCountNeverHidesOne()
    {
        await using var feature = new MigrationFeature();
        feature.Records.Kept = MigrationFeature.Carried(
            MigrationPass.ForReal,
            [
                .. Enumerable.Range(0, 5).Select(number => MigrationVerdict.Refuse(
                    MigrationPopulation.RecordingFiles,
                    $"stray-{number}.m2ts",
                    MigrationRefusal.Orphan,
                    $"stray-{number}.m2ts",
                    null,
                    number)),
            ]);

        List<string> walked = [];

        for (int page = 1; page <= 3; page++)
        {
            (_, JsonElement body) = await feature.GetAsync($"{Record}?page={page}&perPage=2");
            JsonElement data = body.GetProperty("data");

            Assert.Equal(5, data.GetProperty("total").GetInt32());
            Assert.Equal(3, data.GetProperty("lastPage").GetInt32());
            Assert.Equal(page, data.GetProperty("currentPage").GetInt32());
            Assert.Equal(2, data.GetProperty("perPage").GetInt32());

            walked.AddRange(data.GetProperty("items").EnumerateArray()
                .Select(row => row.GetProperty("subject").GetString()!));
        }

        Assert.Equal([1, 2, 3], feature.Records.PagesAsked);
        Assert.Equal(5, walked.Count);
        Assert.Equal(walked.Count, walked.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task APageSizeBeyondWhatTheRecordHandsOutIsCutDownAndSaysWhatItUsed()
    {
        await using var feature = new MigrationFeature();
        feature.Records.Kept = MigrationFeature.Carried(MigrationPass.Rehearsal);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(
            $"{Record}?perPage={MigrationDetailQuery.MostPerPage + 1}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            MigrationDetailQuery.MostPerPage,
            body.GetProperty("data").GetProperty("perPage").GetInt32());
    }

    [Fact]
    public async Task APageBeforeTheFirstIsRefusedRatherThanAnsweredAsTheFirst()
    {
        await using var feature = new MigrationFeature();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"{Record}?page=0");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.False(body.GetProperty("status").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").ValueKind);
    }
}
