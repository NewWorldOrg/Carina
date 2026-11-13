using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Integrity;
using Carina.TestSupport;

namespace Carina.Api.Tests.Feature.Recordings;

[Trait("Category", "Feature")]
public sealed class RunRecordingIntegrityCheckActionTests
{
    private static readonly RecordingId Kept = new(new Guid("7a3e5c1b-0000-0000-0000-0000000000a1"));

    private static readonly RecordingId Gone = new(new Guid("7a3e5c1b-0000-0000-0000-0000000000a2"));

    private static readonly RecordingId Writing = new(new Guid("7a3e5c1b-0000-0000-0000-0000000000a3"));

    private static readonly OutputRoot Encodes = new("encodes");

    private static readonly string KeptFile = Kept.Wire + ".ts";

    private static readonly string KeptArtefact = Kept.Wire + ".0000000a000000000000000000000001.mp4";

    private static readonly string GoneArtefact = Gone.Wire + ".0000000a000000000000000000000001.mp4";

    private static readonly string LeftWork = Gone.Wire + ".0000000b000000000000000000000001.attempt1.encoding";

    [Fact]
    public async Task ARootThatCouldNotBeReadIsCountedAsOutOfReachRatherThanJudgedAndFoundEmpty()
    {
        using var store = new RecordingStore();
        await using var feature = new IntegrityFeature(walking: Path.Combine(store.Root, "not-mounted"));
        feature.Ledger.Rows.Add(Ended(RecordingId.New(), "one.m2ts", LedgerClaim.EverythingLanded, 100));

        await feature.PostAsync("/api/recordings/integrity/run");
        (_, JsonElement page) = await feature.GetAsync("/api/recordings/integrity");
        JsonElement check = page.GetProperty("data").GetProperty("check");

        Assert.Equal(0, check.GetProperty("rootsWalked").GetInt32());
        Assert.Equal(1, check.GetProperty("rootsOutOfReach").GetInt32());
        Assert.Equal(1, check.GetProperty("ledgerRowsRead").GetInt32());
        Assert.Equal(0, check.GetProperty("ledgerRowsJudged").GetInt32());
        Assert.Equal(1, check.GetProperty("ledgerRowsInRootsOutOfReach").GetInt32());
        Assert.Empty(page.GetProperty("data").GetProperty("items").EnumerateArray());
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
    public async Task ASweepWalkingIsNamedRatherThanTheHoldBackWhenBothWouldRefuse()
    {
        await using var feature = new IntegrityFeature();
        IntegrityCheckId earlier = IntegrityCheckId.New();
        Save(
            feature,
            earlier,
            IntegrityFinding.NoLedgerRow(
                earlier,
                IntegrityFeature.Primary,
                "written-earlier.m2ts",
                1,
                IntegrityFeature.Noon));
        feature.Ledger.Gate = new TaskCompletionSource();

        Task<IntegrityRun> timetabled = feature.Sweeps.RunAsync(CancellationToken.None);
        await Eventually.Happens(() => feature.Ledger.Reads is 1, "the timetabled sweep never read the ledger");

        (HttpStatusCode status, JsonElement body) = await feature
            .PostAsync("/api/recordings/integrity/run")
            .WaitAsync(Eventually.Patience);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("oneIsAlreadyRunning", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            body.GetProperty("data").GetProperty("notBefore").ValueKind);
        Assert.Equal(
            feature.Sweeps.RunningCheck!.Value,
            body.GetProperty("data").GetProperty("runningCheckId").GetGuid());

        feature.Ledger.Gate.SetResult();
        await timetabled;
    }

    [Fact]
    public async Task TheHoldBackAloneWouldHaveRefusedThatSameRequest()
    {
        await using var feature = new IntegrityFeature();
        IntegrityCheckId earlier = IntegrityCheckId.New();
        Save(
            feature,
            earlier,
            IntegrityFinding.NoLedgerRow(
                earlier,
                IntegrityFeature.Primary,
                "written-earlier.m2ts",
                1,
                IntegrityFeature.Noon));

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/recordings/integrity/run");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("tooSoonAfterTheLastOne", body.GetProperty("data").GetProperty("refusal").GetString());
    }

    [Fact]
    public async Task ASweepThatThrewLeavesNothingBehindThatStopsTheNextOne()
    {
        await using var feature = new IntegrityFeature();
        feature.Ledger.Throws = new InvalidOperationException("the ledger could not be read");

        using HttpResponseMessage broke = await feature.Client.PostAsync(
            new Uri("/api/recordings/integrity/run", UriKind.Relative),
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

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
            Content = new StringContent("run=1", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded"),
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

    [Fact]
    public async Task WhatNothingClaimsInTheEncodeRootAndTheThumbnailsIsListedAndTheRecordingIsNot()
    {
        using var recordings = new RecordingStore();
        using var encodes = new RecordingStore();
        using var pictures = new RecordingStore();
        recordings.Holding(KeptFile, 400);
        encodes.Holding(KeptArtefact, 90).Holding(GoneArtefact, 80).Holding(LeftWork, 7);
        pictures.Holding(Kept.Wire + ".jpg", 30).Holding(Gone.Wire + ".jpg", 20);
        await using IntegrityFeature feature = Walking(recordings, encodes, pictures);

        (HttpStatusCode ran, _) = await feature.PostAsync("/api/recordings/integrity/run");
        (_, JsonElement page) = await feature.GetAsync("/api/recordings/integrity");

        Assert.Equal(HttpStatusCode.OK, ran);
        Assert.Equal(
            [
                $"encodes/{GoneArtefact} noLedgerRow",
                $"encodes/{LeftWork} noLedgerRow",
                $"thumbnails/{Gone.Wire}.jpg noLedgerRow",
            ],
            Listed(page));
    }

    [Fact]
    public async Task AnEncodeRootSetOnTheRecordingRootNeverCallsTheRecordingsOrphans()
    {
        using var recordings = new RecordingStore();
        using var pictures = new RecordingStore();
        recordings.Holding(KeptFile, 400).Holding("left.tmp", 3);
        await using IntegrityFeature feature = Walking(recordings, recordings, pictures);

        (HttpStatusCode ran, _) = await feature.PostAsync("/api/recordings/integrity/run");
        (_, JsonElement page) = await feature.GetAsync("/api/recordings/integrity");

        Assert.Equal(HttpStatusCode.OK, ran);
        Assert.Equal(["primary/left.tmp noLedgerRow"], Listed(page));
    }

    [Fact]
    public async Task AnEncodeRootThatShowsTheRecordingsUnderAnotherPathIsNotJudged()
    {
        using var recordings = new RecordingStore();
        using var mirrored = new RecordingStore();
        using var pictures = new RecordingStore();
        recordings.Holding(KeptFile, 400);
        mirrored.Holding(KeptFile, 400).Holding(GoneArtefact, 80);
        await using IntegrityFeature feature = Walking(recordings, mirrored, pictures);

        (HttpStatusCode ran, _) = await feature.PostAsync("/api/recordings/integrity/run");
        (_, JsonElement page) = await feature.GetAsync("/api/recordings/integrity");

        Assert.Equal(HttpStatusCode.OK, ran);
        Assert.Empty(Listed(page));
        Assert.Equal(1, feature.Checks.Saved[^1].Check.RootsOutOfReach);
    }

    [Fact]
    public async Task PicturesDrawnIntoTheEncodeRootAreNotOrphansWhileTheirRecordingsAreWrittenDown()
    {
        using var recordings = new RecordingStore();
        using var shared = new RecordingStore();
        recordings.Holding(KeptFile, 400);
        shared.Holding(KeptArtefact, 90)
            .Holding(Kept.Wire + ".jpg", 30)
            .Holding(Writing.Wire + ".jpg", 25)
            .Holding(Gone.Wire + ".jpg", 20);
        await using IntegrityFeature feature = Walking(recordings, shared, shared);
        feature.Ledger.Rows.Add(
            LedgerFile.StillWriting(Writing, IntegrityFeature.Primary, new RecordingFileName(Writing.Wire + ".ts")));

        (HttpStatusCode ran, _) = await feature.PostAsync("/api/recordings/integrity/run");
        (_, JsonElement page) = await feature.GetAsync("/api/recordings/integrity");

        Assert.Equal(HttpStatusCode.OK, ran);
        Assert.Equal([$"encodes/{Gone.Wire}.jpg noLedgerRow"], Listed(page));
    }

    private static IntegrityFeature Walking(
        RecordingStore recordings,
        RecordingStore encodes,
        RecordingStore pictures,
        RecordingStore? captions = null)
    {
        var feature = new IntegrityFeature(
            walking: recordings.Root,
            encoding: new EncodeSettings { OutputRoots = [new StorageRootPath(Encodes, encodes.Root)] },
            drawing: pictures.Root,
            captioning: captions?.Root);

        feature.Ledger.Rows.Add(LedgerFile.Ended(
            Kept,
            IntegrityFeature.Primary,
            new RecordingFileName(KeptFile),
            LedgerClaim.EverythingLanded,
            400));
        feature.Working.Files.Add(new DeclaredFile(Encodes, KeptArtefact));

        return feature;
    }

    private static string[] Listed(JsonElement page)
        => [.. page.GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(item => $"{item.GetProperty("outputRoot").GetString()}/{item.GetProperty("path").GetString()} "
                            + item.GetProperty("fault").GetString())
            .Order(StringComparer.Ordinal)];
}
