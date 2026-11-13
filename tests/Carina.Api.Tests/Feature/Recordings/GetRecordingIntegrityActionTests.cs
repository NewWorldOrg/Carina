using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;

namespace Carina.Api.Tests.Feature.Recordings;

[Trait("Category", "Feature")]
public sealed class GetRecordingIntegrityActionTests
{
    private static readonly RecordingFileName Name = new("one.m2ts");

    [Fact]
    public async Task NothingHasBeenCheckedYetIsAnsweredAsAnEmptyPageRatherThanARefusal()
    {
        await using var feature = new IntegrityFeature();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/recordings/integrity");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("check").ValueKind);
        Assert.Empty(data.GetProperty("items").EnumerateArray());
        Assert.Equal(0, data.GetProperty("total").GetInt32());
        Assert.Equal(1, data.GetProperty("lastPage").GetInt32());
    }

    [Fact]
    public async Task AskingForTheFindingsReachesTheIntegrityEndpointRatherThanTheOneNamingARecording()
    {
        await using var feature = new IntegrityFeature();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/recordings/integrity");
        (HttpStatusCode named, _) = await feature.GetAsync("/api/recordings/integrity-is-not-an-id");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("data").TryGetProperty("check", out _));
        Assert.Equal(HttpStatusCode.BadRequest, named);
    }

    [Fact]
    public async Task WhatTheSweepFoundComesBackFromTheTableItWasWrittenTo()
    {
        using var store = new RecordingStore();
        store.Holding("stray.m2ts", 400);
        await using var feature = new IntegrityFeature(walking: store.Root);

        (HttpStatusCode ran, JsonElement run) = await feature.PostAsync("/api/recordings/integrity/run");
        (HttpStatusCode listed, JsonElement page) = await feature.GetAsync("/api/recordings/integrity");

        Assert.Equal(HttpStatusCode.OK, ran);
        Assert.Equal(HttpStatusCode.OK, listed);

        IntegrityReport saved = Assert.Single(feature.Checks.Saved);
        JsonElement item = Assert.Single(page.GetProperty("data").GetProperty("items").EnumerateArray());

        Assert.Equal(saved.Check.Id.Value, run.GetProperty("data").GetProperty("check").GetProperty("id").GetGuid());
        Assert.Equal(1, run.GetProperty("data").GetProperty("findings").GetInt32());
        Assert.Equal(
            saved.Check.Id.Value,
            page.GetProperty("data").GetProperty("check").GetProperty("id").GetGuid());
        Assert.Equal(saved.Findings[0].Id.Value, item.GetProperty("id").GetGuid());
        Assert.Equal("noLedgerRow", item.GetProperty("fault").GetString()!);
        Assert.Equal("stray.m2ts", item.GetProperty("path").GetString()!);
        Assert.Equal("primary", item.GetProperty("outputRoot").GetString());
        Assert.Equal(400, item.GetProperty("observedSize").GetInt64());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("recordingId").ValueKind);
    }

    [Fact]
    public async Task AFindingAboutARecordingCarriesTheNameThatRecordingIsAskedForBy()
    {
        await using var feature = new IntegrityFeature();
        var id = new RecordingId(new Guid("2f4a6c8e-0000-0000-0000-00000000000b"));
        IntegrityCheckId check = IntegrityCheckId.New();

        Save(feature, check, IntegrityFinding.SizeDisagrees(
            check,
            IntegrityFeature.Primary,
            id,
            Name,
            1_000,
            999,
            IntegrityFeature.Noon));

        (_, JsonElement page) = await feature.GetAsync("/api/recordings/integrity");
        JsonElement item = Assert.Single(page.GetProperty("data").GetProperty("items").EnumerateArray());

        Assert.Equal("sizeDisagrees", item.GetProperty("fault").GetString()!);
        Assert.Equal(id.Wire, item.GetProperty("recordingId").GetString());
        Assert.Equal(1_000, item.GetProperty("ledgerSize").GetInt64());
        Assert.Equal(999, item.GetProperty("observedSize").GetInt64());
    }

    [Fact]
    public async Task EveryClassTheSweepCanNameIsSpelledTheWayTheDocumentSpellsIt()
    {
        await using var feature = new IntegrityFeature();
        IntegrityCheckId check = IntegrityCheckId.New();
        var id = new RecordingId(new Guid("2f4a6c8e-0000-0000-0000-00000000000c"));

        Save(
            feature,
            check,
            IntegrityFinding.SizeDisagrees(check, IntegrityFeature.Primary, id, new RecordingFileName("a.m2ts"), 1, 2, IntegrityFeature.Noon),
            IntegrityFinding.NoLedgerRow(check, IntegrityFeature.Primary, "b.m2ts", 3, IntegrityFeature.Noon),
            IntegrityFinding.FileMissing(check, IntegrityFeature.Primary, id, new RecordingFileName("c.m2ts"), 4, IntegrityFeature.Noon),
            IntegrityFinding.FileEmpty(check, IntegrityFeature.Primary, id, new RecordingFileName("d.m2ts"), 5, 0, IntegrityFeature.Noon),
            IntegrityFinding.EmptyThoughComplete(check, IntegrityFeature.Primary, id, new RecordingFileName("e.m2ts"), 6, 0, IntegrityFeature.Noon),
            IntegrityFinding.ThumbnailMissing(check, new DrawnPicture(id, IntegrityFeature.Primary, "f.jpg"), IntegrityFeature.Noon));

        (_, JsonElement page) = await feature.GetAsync("/api/recordings/integrity?perPage=200");

        Assert.Equal(
            ["sizeDisagrees", "noLedgerRow", "fileMissing", "fileEmpty", "emptyThoughComplete", "thumbnailMissing"],
            page.GetProperty("data").GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("fault").GetString()!)
                .ToArray());
        Assert.Equal(
            Enum.GetValues<IntegrityFault>().Length,
            page.GetProperty("data").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task TheCheckBesideTheFindingsSaysHowMuchOfTheLedgerAndTheDiskItRead()
    {
        using var store = new RecordingStore();
        store.Holding("stray.m2ts", 400).Holding("nested/deeper.m2ts", 500);
        await using var feature = new IntegrityFeature(walking: store.Root);
        feature.Ledger.Rows.Add(LedgerFile.Ended(
            RecordingId.New(),
            IntegrityFeature.Primary,
            Name,
            LedgerClaim.EverythingLanded,
            100));

        await feature.PostAsync("/api/recordings/integrity/run");
        (_, JsonElement page) = await feature.GetAsync("/api/recordings/integrity");
        JsonElement check = page.GetProperty("data").GetProperty("check");

        Assert.Equal(1, check.GetProperty("rootsWalked").GetInt32());
        Assert.Equal(0, check.GetProperty("rootsOutOfReach").GetInt32());
        Assert.Equal(2, check.GetProperty("filesRead").GetInt32());
        Assert.Equal(1, check.GetProperty("ledgerRowsRead").GetInt32());
        Assert.Equal(1, check.GetProperty("ledgerRowsJudged").GetInt32());
        Assert.Equal(0, check.GetProperty("ledgerRowsStillWriting").GetInt32());
        Assert.Equal(0, check.GetProperty("ledgerRowsInRootsOutOfReach").GetInt32());
    }

    [Fact]
    public async Task ThePageSaysHowManyFindingsThereAreAndWhichPageThisIs()
    {
        await using var feature = new IntegrityFeature();
        IntegrityCheckId check = IntegrityCheckId.New();

        Save(
            feature,
            check,
            IntegrityFinding.NoLedgerRow(check, IntegrityFeature.Primary, "a.m2ts", 1, IntegrityFeature.Noon),
            IntegrityFinding.NoLedgerRow(check, IntegrityFeature.Primary, "b.m2ts", 2, IntegrityFeature.Noon),
            IntegrityFinding.NoLedgerRow(check, IntegrityFeature.Primary, "c.m2ts", 3, IntegrityFeature.Noon));

        (_, JsonElement first) = await feature.GetAsync("/api/recordings/integrity?perPage=2");
        (_, JsonElement second) = await feature.GetAsync("/api/recordings/integrity?perPage=2&page=2");

        Assert.Equal(3, first.GetProperty("data").GetProperty("total").GetInt32());
        Assert.Equal(2, first.GetProperty("data").GetProperty("lastPage").GetInt32());
        Assert.Equal(2, first.GetProperty("data").GetProperty("perPage").GetInt32());
        Assert.Equal(
            ["a.m2ts", "b.m2ts"],
            first.GetProperty("data").GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("path").GetString()!)
                .ToArray());
        Assert.Equal(
            ["c.m2ts"],
            second.GetProperty("data").GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("path").GetString()!)
                .ToArray());
    }

    [Fact]
    public async Task APageSizeOverTheCeilingIsCutDownToItAndAnsweredAsTheSizeThatWasUsed()
    {
        await using var feature = new IntegrityFeature();
        IntegrityCheckId check = IntegrityCheckId.New();

        Save(
            feature,
            check,
            [.. Enumerable
                .Range(1, IntegrityFindingQuery.MostPerPage + 1)
                .Select(seed => IntegrityFinding.NoLedgerRow(
                    check,
                    IntegrityFeature.Primary,
                    $"stray-{seed:0000}.m2ts",
                    seed,
                    IntegrityFeature.Noon))]);

        (HttpStatusCode status, JsonElement page) = await feature.GetAsync(
            $"/api/recordings/integrity?perPage={IntegrityFindingQuery.MostPerPage + 1}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            IntegrityFindingQuery.MostPerPage + 1,
            page.GetProperty("data").GetProperty("total").GetInt32());
        Assert.Equal(
            IntegrityFindingQuery.MostPerPage,
            page.GetProperty("data").GetProperty("perPage").GetInt32());
        Assert.Equal(
            IntegrityFindingQuery.MostPerPage,
            page.GetProperty("data").GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    public async Task APageBeforeTheFirstOneIsRefused(string query)
    {
        await using var feature = new IntegrityFeature();

        (HttpStatusCode status, _) = await feature.GetAsync("/api/recordings/integrity?" + query);

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task AskingForTheFindingsLeavesEveryRecordingOnTheDiskWhereItWas()
    {
        using var store = new RecordingStore();
        store.Holding("agrees.m2ts", 400).Holding("stray.m2ts", 4_000);

        await using var feature = new IntegrityFeature(walking: store.Root);
        await feature.PostAsync("/api/recordings/integrity/run");

        IReadOnlyList<string> before = store.Fingerprint();

        (HttpStatusCode status, JsonElement page) = await feature.GetAsync("/api/recordings/integrity");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(2, before.Count);
        Assert.Equal(2, page.GetProperty("data").GetProperty("total").GetInt32());
        Assert.Equal(before, store.Fingerprint());
    }

    private static void Save(IntegrityFeature feature, IntegrityCheckId check, params IntegrityFinding[] findings)
        => feature.Checks.Saved.Add(IntegrityReport.Of(
            IntegrityCheck.Rehydrate(
                check,
                IntegrityFeature.Noon,
                IntegrityFeature.Noon.AddSeconds(1),
                1,
                0,
                findings.Length,
                0,
                0,
                0,
                0),
            findings));
}
