using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.TestSupport;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class IntegrityEndpointTests
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
            IntegrityFinding.EmptyThoughComplete(check, IntegrityFeature.Primary, id, new RecordingFileName("e.m2ts"), 6, 0, IntegrityFeature.Noon));

        (_, JsonElement page) = await feature.GetAsync("/api/recordings/integrity?perPage=200");

        Assert.Equal(
            ["sizeDisagrees", "noLedgerRow", "fileMissing", "fileEmpty", "emptyThoughComplete"],
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
    public async Task ASweepAskedForByHandRunsAndSaysWhatItFound()
    {
        using var store = new RecordingStore();
        store.Holding("stray.m2ts", 400);
        await using var feature = new IntegrityFeature(walking: store.Root);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/recordings/integrity/run");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("status").GetBoolean());
        Assert.Equal(1, body.GetProperty("data").GetProperty("findings").GetInt32());
        Assert.Equal(1, body.GetProperty("data").GetProperty("check").GetProperty("filesRead").GetInt32());
        Assert.Single(feature.Checks.Saved);
    }

    [Fact]
    public async Task ASecondSweepAskedForTooSoonIsRefusedAndSaysWhenItMayBeAskedForAgain()
    {
        await using var feature = new IntegrityFeature();

        Assert.Equal(HttpStatusCode.OK, (await feature.PostAsync("/api/recordings/integrity/run")).Status);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/recordings/integrity/run");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.False(body.GetProperty("status").GetBoolean());
        Assert.Equal("tooSoonAfterTheLastOne", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.Equal(
            IntegrityFeature.Noon + feature.Settings.BetweenManualSweeps,
            body.GetProperty("data").GetProperty("notBefore").GetDateTimeOffset().UtcDateTime);
        Assert.Equal(
            JsonValueKind.Null,
            body.GetProperty("data").GetProperty("runningCheckId").ValueKind);
        Assert.Single(feature.Checks.Saved);
    }

    [Fact]
    public async Task ASweepAskedForOnceTheHoldBackHasPassedRunsAgain()
    {
        await using var feature = new IntegrityFeature();

        await feature.PostAsync("/api/recordings/integrity/run");
        feature.Clock.Now = IntegrityFeature.Noon + feature.Settings.BetweenManualSweeps;

        (HttpStatusCode status, _) = await feature.PostAsync("/api/recordings/integrity/run");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(2, feature.Checks.Saved.Count);
    }

    [Fact]
    public async Task ASweepAskedForOneTickBeforeTheHoldBackHasPassedIsStillRefused()
    {
        await using var feature = new IntegrityFeature();

        await feature.PostAsync("/api/recordings/integrity/run");
        feature.Clock.Now = IntegrityFeature.Noon + feature.Settings.BetweenManualSweeps - TimeSpan.FromTicks(1);

        (HttpStatusCode status, _) = await feature.PostAsync("/api/recordings/integrity/run");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Single(feature.Checks.Saved);
    }

    [Fact]
    public async Task ASweepAskedForWhileOneIsWalkingIsRefusedAndTheRefusalNamesTheOneWalking()
    {
        await using var feature = new IntegrityFeature();
        feature.Ledger.Gate = new TaskCompletionSource();

        Task<(HttpStatusCode Status, JsonElement Body)> first =
            feature.PostAsync("/api/recordings/integrity/run");
        await Eventually.Happens(() => feature.Ledger.Reads is 1, "the first sweep never reached the ledger");

        (HttpStatusCode status, JsonElement body) = await feature
            .PostAsync("/api/recordings/integrity/run")
            .WaitAsync(Eventually.Patience);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("oneIsAlreadyRunning", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            body.GetProperty("data").GetProperty("notBefore").ValueKind);

        feature.Ledger.Gate.SetResult();
        await first;

        Assert.Equal(
            feature.Checks.Saved[0].Check.Id.Value,
            body.GetProperty("data").GetProperty("runningCheckId").GetGuid());
    }

    [Fact]
    public async Task ASweepThatThrewLeavesNothingBehindThatStopsTheNextOne()
    {
        await using var feature = new IntegrityFeature();
        feature.Ledger.Throws = new InvalidOperationException("the ledger could not be read");

        using HttpResponseMessage broke = await feature.Client.PostAsync(
            new Uri("/api/recordings/integrity/run", UriKind.Relative),
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.InternalServerError, broke.StatusCode);

        feature.Ledger.Throws = null;

        (HttpStatusCode status, _) = await feature.PostAsync("/api/recordings/integrity/run");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Single(feature.Checks.Saved);
    }

    [Fact]
    public async Task ASweepAskedForWithoutJsonIsRefusedBeforeItReachesTheLedger()
    {
        await using var feature = new IntegrityFeature();

        using var asking = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/api/recordings/integrity/run", UriKind.Relative))
        {
            Content = new StringContent("run=1", Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        using HttpResponseMessage response = await feature.Client.SendAsync(asking);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(0, feature.Ledger.Reads);
        Assert.Empty(feature.Checks.Saved);
    }

    [Fact]
    public async Task ASweepLeavesEveryRecordingOnTheDiskExactlyWhereItWas()
    {
        using var store = new RecordingStore();
        store
            .Holding("agrees.m2ts", 400)
            .Holding("disagrees.m2ts", 300)
            .Holding("empty.m2ts", 0)
            .Holding("stray.m2ts", 4_000)
            .Holding("nested/buried.m2ts", 40_000);

        await using var feature = new IntegrityFeature(walking: store.Root);
        RecordingId agrees = RecordingId.New();
        RecordingId disagrees = RecordingId.New();
        RecordingId empty = RecordingId.New();
        RecordingId gone = RecordingId.New();

        feature.Ledger.Rows.AddRange(
            Ended(agrees, "agrees.m2ts", LedgerClaim.EverythingLanded, 400),
            Ended(disagrees, "disagrees.m2ts", LedgerClaim.EverythingLanded, 999),
            Ended(empty, "empty.m2ts", LedgerClaim.EverythingLanded, 500),
            Ended(gone, "gone.m2ts", LedgerClaim.EverythingLanded, 600));

        IReadOnlyList<string> before = store.Fingerprint();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/recordings/integrity/run");
        (_, JsonElement page) = await feature.GetAsync("/api/recordings/integrity?perPage=200");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(6, before.Count);
        Assert.Equal(5, body.GetProperty("data").GetProperty("check").GetProperty("filesRead").GetInt32());
        Assert.Equal(
            ["emptyThoughComplete", "fileMissing", "noLedgerRow", "noLedgerRow", "sizeDisagrees"],
            page.GetProperty("data").GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("fault").GetString()!)
                .Order(StringComparer.Ordinal)
                .ToArray());
        Assert.Equal(before, store.Fingerprint());
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

    [Fact]
    public async Task TheFingerprintOfTheStoreWouldNoticeAFileGoingMissingOrChanging()
    {
        using var store = new RecordingStore();
        store.Holding("agrees.m2ts", 400).Holding("stray.m2ts", 4_000);

        IReadOnlyList<string> before = store.Fingerprint();

        store.Holding("agrees.m2ts", 401);

        Assert.NotEqual(before, store.Fingerprint());

        File.Delete(Path.Combine(store.Root, "stray.m2ts"));

        Assert.NotEqual(before, store.Fingerprint());
        Assert.Single(store.Fingerprint());
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

    private static LedgerFile Ended(RecordingId id, string fileName, LedgerClaim claim, long size)
        => LedgerFile.Ended(id, IntegrityFeature.Primary, new RecordingFileName(fileName), claim, size);
}
