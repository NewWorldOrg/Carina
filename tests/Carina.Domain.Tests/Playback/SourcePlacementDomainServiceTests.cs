using Carina.Domain.Encodings;
using Carina.Domain.Playback;
using Carina.Domain.Recordings;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Playback;

public sealed class SourcePlacementDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Queued = new(2026, 9, 4, 3, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Begins = TimeSpan.FromSeconds(30499.474078);

    private static readonly TimeSpan HeadSkip = TimeSpan.FromSeconds(0.5072);

    private static readonly TimeSpan ArtefactLength = TimeSpan.FromSeconds(2096.8);

    private static readonly PlaybackFile Recorded =
        new(new OutputRoot("bulk"), new RecordingFileName("a1b2c3.m2ts"), 4_000_000);

    private static readonly PlaybackPlan Transcoded = PlaybackPlan.For(
        PlaybackSubject.NothingHasBeenEncodedYet(RecordingOutcome.Complete, PlaybackFileSearch.Of(Recorded)));

    private static readonly PlaybackPlan HandedOver = PlaybackPlan.For(new PlaybackSubject(
        RecordingOutcome.Complete,
        PlaybackFileSearch.Of(Recorded),
        [PlaybackFileSearch.Of(new PlaybackFile(new OutputRoot("shelf"), new RecordingFileName("encoded.mp4"), 1_000_000))]));

    private readonly HeldEncodeJobs jobs = new();

    private SourcePlacementDomainService Service => new(jobs);

    [Fact(DisplayName = "BR-PD-017: what is placed on a recording played as it was counts from where the file's own clock began, and says no length")]
    public async Task WhatIsPlacedOnTheRecordingCountsFromItsOwnClock()
    {
        EncodeJob made = Made(Begins);
        jobs.Jobs.Add(made);

        SourceAlignment alignment = await Service.PlaceAsync(Transcoded, made.Id, Begins, Cancel);

        Assert.Equal(new SourceZero(Begins, null), alignment.Zero);
        Assert.Null(alignment.Artefact);
        Assert.Null(alignment.ArtefactBegan);
    }

    [Fact(DisplayName = "BR-PD-017: what is placed on an artefact counts from where its job began reading plus the head it skipped, and lasts as long as the artefact")]
    public async Task WhatIsPlacedOnAnArtefactCountsFromWhereItsJobBegan()
    {
        EncodeJob made = Made(Begins);
        jobs.Jobs.Add(made);

        SourceAlignment alignment = await Service.PlaceAsync(HandedOver, made.Id, Begins, Cancel);

        Assert.Equal(new SourceZero(Begins + HeadSkip, ArtefactLength), alignment.Zero);
        Assert.Null(alignment.Artefact);
    }

    [Fact(DisplayName = "BR-PD-017: a clock that began less than a millisecond from where the artefact's job began reading still places on the artefact")]
    public async Task AClockJustUnderAMillisecondAwayStillPlaces()
    {
        EncodeJob made = Made(Begins + EncodeTimeline.ClocksAgreeWithin - TimeSpan.FromTicks(1));
        jobs.Jobs.Add(made);

        SourceAlignment alignment = await Service.PlaceAsync(HandedOver, made.Id, Begins, Cancel);

        Assert.NotNull(alignment.Zero);
    }

    [Theory(DisplayName = "BR-PD-017: what was taken from a file whose clock began a millisecond or more away is placed on no artefact, and the artefact is named")]
    [InlineData(1)]
    [InlineData(-1)]
    public async Task AClockAMillisecondAwayPlacesNothing(int direction)
    {
        TimeSpan encodedFrom = Begins + (direction * EncodeTimeline.ClocksAgreeWithin);
        EncodeJob made = Made(encodedFrom);
        jobs.Jobs.Add(made);

        SourceAlignment alignment = await Service.PlaceAsync(HandedOver, made.Id, Begins, Cancel);

        Assert.Null(alignment.Zero);
        Assert.Equal(made.Id, alignment.Artefact);
        Assert.Equal(encodedFrom, alignment.ArtefactBegan);
    }

    [Fact(DisplayName = "BR-PD-017: an artefact that is not named, not in the ledger, or whose job kept no timeline has nothing placed on it")]
    public async Task AnArtefactWithoutATimelineHasNothingPlacedOnIt()
    {
        EncodeJob untimed = Made(null);
        jobs.Jobs.Add(untimed);

        Assert.Same(SourceAlignment.Unplaced, await Service.PlaceAsync(HandedOver, null, Begins, Cancel));
        Assert.Same(SourceAlignment.Unplaced, await Service.PlaceAsync(HandedOver, EncodeJobId.New(), Begins, Cancel));
        Assert.Same(SourceAlignment.Unplaced, await Service.PlaceAsync(HandedOver, untimed.Id, Begins, Cancel));
    }

    private static EncodeJob Made(TimeSpan? sourceStart)
    {
        RecordingId recording = RecordingId.New();
        EncodeProfileId profile = EncodeProfileId.New();

        return EncodeJob.Rehydrate(
            EncodeJobId.New(),
            recording,
            profile,
            EncodeDestinationId.New(),
            new OutputRoot("shelf"),
            EncodeJobStatus.Completed,
            EncodeJob.FirstAttempt,
            Queued,
            Queued.AddSeconds(5),
            Queued.AddHours(1),
            null,
            EncodeFileName.Artefact(recording, profile),
            null,
            null,
            null,
            sourceStart is { } start ? new EncodeTimeline(start, HeadSkip, null, ArtefactLength) : null,
            null);
    }
}
