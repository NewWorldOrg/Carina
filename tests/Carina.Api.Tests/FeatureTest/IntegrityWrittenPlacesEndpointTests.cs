using System.Net;
using System.Text.Json;

using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;

namespace Carina.Api.Tests.FeatureTest;

public sealed class IntegrityWrittenPlacesEndpointTests
{
    private static readonly RecordingId Kept = new(new Guid("7a3e5c1b-0000-0000-0000-0000000000a1"));

    private static readonly RecordingId Gone = new(new Guid("7a3e5c1b-0000-0000-0000-0000000000a2"));

    private static readonly OutputRoot Encodes = new("encodes");

    private static readonly string KeptFile = Kept.Wire + ".ts";

    private static readonly string KeptArtefact = Kept.Wire + ".0000000a000000000000000000000001.mp4";

    private static readonly string GoneArtefact = Gone.Wire + ".0000000a000000000000000000000001.mp4";

    private static readonly string LeftWork = Gone.Wire + ".0000000b000000000000000000000001.attempt1.encoding";

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
    public async Task APictureNothingClaimsIsThrownAwayByThisProcessAndNothingBesideItIsTouched()
    {
        using var recordings = new RecordingStore();
        using var encodes = new RecordingStore();
        using var pictures = new RecordingStore();
        recordings.Holding(KeptFile, 400);
        pictures.Holding(Kept.Wire + ".jpg", 30).Holding(Gone.Wire + ".jpg", 20);
        await using IntegrityFeature feature = Walking(recordings, encodes, pictures);
        IReadOnlyList<string> recordingsBefore = recordings.Fingerprint();
        IntegrityFinding finding = await CheckedAsync(feature, Gone.Wire + ".jpg");

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            $"/api/recordings/integrity/findings/{finding.Id.Value}/delete");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("data").GetProperty("fileRemoved").GetBoolean());
        Assert.Equal("thumbnails", body.GetProperty("data").GetProperty("outputRoot").GetString());
        Assert.False(File.Exists(Path.Combine(pictures.Root, Gone.Wire + ".jpg")));
        Assert.True(File.Exists(Path.Combine(pictures.Root, Kept.Wire + ".jpg")));
        Assert.Equal(recordingsBefore, recordings.Fingerprint());
        Assert.Empty(feature.Driver.AskedAboutStrays);
    }

    [Fact]
    public async Task AnArtefactThatChangedSinceTheCheckIsRefusedAndLeftAsItNowIs()
    {
        using var recordings = new RecordingStore();
        using var encodes = new RecordingStore();
        using var pictures = new RecordingStore();
        recordings.Holding(KeptFile, 400);
        encodes.Holding(GoneArtefact, 80);
        await using IntegrityFeature feature = Walking(recordings, encodes, pictures);
        IntegrityFinding finding = await CheckedAsync(feature, GoneArtefact);
        await File.AppendAllTextAsync(Path.Combine(encodes.Root, GoneArtefact), "more");

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            $"/api/recordings/integrity/findings/{finding.Id.Value}/delete");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("fileChanged", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.Equal(84, new FileInfo(Path.Combine(encodes.Root, GoneArtefact)).Length);
    }

    [Fact]
    public async Task APictureWhoseRecordingIsWrittenDownSinceTheCheckIsRefusedAndKept()
    {
        using var recordings = new RecordingStore();
        using var encodes = new RecordingStore();
        using var pictures = new RecordingStore();
        recordings.Holding(KeptFile, 400);
        pictures.Holding(Gone.Wire + ".jpg", 20);
        await using IntegrityFeature feature = Walking(recordings, encodes, pictures);
        IntegrityFinding finding = await CheckedAsync(feature, Gone.Wire + ".jpg");
        feature.Ledger.Rows.Add(
            LedgerFile.StillWriting(Gone, IntegrityFeature.Primary, new RecordingFileName(Gone.Wire + ".ts")));

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            $"/api/recordings/integrity/findings/{finding.Id.Value}/delete");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("fileChanged", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.True(File.Exists(Path.Combine(pictures.Root, Gone.Wire + ".jpg")));
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

    private static IntegrityFeature Walking(RecordingStore recordings, RecordingStore encodes, RecordingStore pictures)
    {
        var feature = new IntegrityFeature(
            walking: recordings.Root,
            encoding: new EncodeSettings { OutputRoots = [new StorageRootPath(Encodes, encodes.Root)] },
            drawing: pictures.Root);

        feature.Ledger.Rows.Add(LedgerFile.Ended(
            Kept,
            IntegrityFeature.Primary,
            new RecordingFileName(KeptFile),
            LedgerClaim.EverythingLanded,
            400));
        feature.Working.Files.Add(new DeclaredFile(Encodes, KeptArtefact));

        return feature;
    }

    private static async Task<IntegrityFinding> CheckedAsync(IntegrityFeature feature, string path)
    {
        (HttpStatusCode ran, _) = await feature.PostAsync("/api/recordings/integrity/run");

        Assert.Equal(HttpStatusCode.OK, ran);

        return feature.Checks.Saved[^1].Findings.Single(finding => finding.Path == path);
    }

    private static string[] Listed(JsonElement page)
        => [.. page.GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(item => $"{item.GetProperty("outputRoot").GetString()}/{item.GetProperty("path").GetString()} "
                            + item.GetProperty("fault").GetString())
            .Order(StringComparer.Ordinal)];
}
