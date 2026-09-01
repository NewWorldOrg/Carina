using Carina.Domain.Channels;
using Carina.Domain.Integrity;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Thumbnails;
using Carina.Infrastructure.Thumbnails;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Thumbnails;

public sealed class ScrubberTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Noon = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

    private static readonly OutputRoot Bulk = new("bulk");

    private static readonly byte[] Picture = [0xff, 0xd8, 0xff];

    [Fact]
    public async Task AFrameIsTakenFromTheRecordedServiceAtThePositionAskedFor()
    {
        var renderer = new HeldRenderer();
        Recording recording = Ended(new ServiceId(23610));

        ScrubFrame frame = await Scrubber(renderer, recording)
            .AtAsync(recording.Id, TimeSpan.FromSeconds(412.5), Cancel);

        ThumbnailFrameRequest asked = Assert.Single(renderer.AskedForAFrame);

        Assert.Equal(23610, asked.Service.Value);
        Assert.Equal(TimeSpan.FromSeconds(412.5), asked.At);
        Assert.Equal("/mounted/bulk/" + recording.FileName.Value, asked.Source);
        Assert.Null(frame.Refusal);
    }

    [Fact]
    public async Task ThePictureTheProgrammeHandedOverIsThePictureThatComesBack()
    {
        var renderer = new HeldRenderer(framed: _ => ThumbnailRender.Drawn(Picture));
        Recording recording = Ended(new ServiceId(1024));

        ScrubFrame frame = await Scrubber(renderer, recording).AtAsync(recording.Id, TimeSpan.Zero, Cancel);

        Assert.Equal(Picture, frame.Picture);
    }

    [Fact]
    public async Task ARecordingNobodyHasHeardOfIsSaidSoAndNothingIsRun()
    {
        var renderer = new HeldRenderer();

        ScrubFrame frame = await Scrubber(renderer).AtAsync(RecordingId.New(), TimeSpan.Zero, Cancel);

        Assert.Equal(ScrubRefusal.NoSuchRecording, frame.Refusal);
        Assert.Empty(renderer.AskedForAFrame);
    }

    [Fact]
    public async Task ARecordingStillBeingWrittenHasNoFrameHandedOutOfIt()
    {
        var renderer = new HeldRenderer();
        Recording recording = Begin(new ServiceId(1024));

        ScrubFrame frame = await Scrubber(renderer, recording).AtAsync(recording.Id, TimeSpan.Zero, Cancel);

        Assert.Equal(ScrubRefusal.StillBeingWritten, frame.Refusal);
        Assert.Empty(renderer.AskedForAFrame);
    }

    [Fact]
    public async Task ARootThisProcessCannotFindIsToldApartFromAPositionThatHoldsNoFrame()
    {
        var renderer = new HeldRenderer();
        Recording recording = Ended(new ServiceId(1024), new OutputRoot("elsewhere"));

        ScrubFrame frame = await Scrubber(renderer, recording).AtAsync(recording.Id, TimeSpan.Zero, Cancel);

        Assert.Equal(ScrubRefusal.SourceOutOfReach, frame.Refusal);
        Assert.Empty(renderer.AskedForAFrame);
    }

    [Fact]
    public async Task APositionPastTheEndIsAnsweredWithNoFrameRatherThanAFrameFromSomewhereElse()
    {
        var renderer = new HeldRenderer(
            framed: _ => ThumbnailRender.Failed(ThumbnailFault.NothingWasWritten, "nothing there"));
        Recording recording = Ended(new ServiceId(1024));

        ScrubFrame frame = await Scrubber(renderer, recording)
            .AtAsync(recording.Id, TimeSpan.FromHours(9), Cancel);

        Assert.Equal(ScrubRefusal.NothingWasDrawn, frame.Refusal);
        Assert.Equal(TimeSpan.FromHours(9), Assert.Single(renderer.AskedForAFrame).At);
    }

    [Fact]
    public async Task AFileTheLedgerNamesAndTheDiskDoesNotIsAnOutOfReachSource()
    {
        var renderer = new HeldRenderer(
            framed: _ => ThumbnailRender.Failed(ThumbnailFault.SourceOutOfReach, "gone"));
        Recording recording = Ended(new ServiceId(1024));

        ScrubFrame frame = await Scrubber(renderer, recording).AtAsync(recording.Id, TimeSpan.Zero, Cancel);

        Assert.Equal(ScrubRefusal.SourceOutOfReach, frame.Refusal);
    }

    [Theory]
    [InlineData(ThumbnailFault.ProgrammeMissing)]
    [InlineData(ThumbnailFault.Refused)]
    [InlineData(ThumbnailFault.TimedOut)]
    public async Task AProgrammeThatIsGoneOrRefusedOrHungLeavesTheCallerWithoutAFrame(ThumbnailFault fault)
    {
        var renderer = new HeldRenderer(
            framed: _ => fault is ThumbnailFault.Refused
                ? ThumbnailRender.Refused(234, "it complained")
                : ThumbnailRender.Failed(fault, "it did not answer"));
        Recording recording = Ended(new ServiceId(1024));

        ScrubFrame frame = await Scrubber(renderer, recording).AtAsync(recording.Id, TimeSpan.Zero, Cancel);

        Assert.Equal(ScrubRefusal.NothingWasDrawn, frame.Refusal);
    }

    [Fact]
    public async Task APositionBeforeTheRecordingBeganIsRefused()
    {
        ArgumentOutOfRangeException refusal = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Scrubber(new HeldRenderer()).AtAsync(RecordingId.New(), TimeSpan.FromSeconds(-1), Cancel));

        Assert.Equal("at", refusal.ParamName);
    }

    [Fact]
    public async Task NoRecordingIdMeansNothingIsLookedUp()
        => Assert.Equal(
            "id",
            (await Assert.ThrowsAsync<ArgumentNullException>(
                () => Scrubber(new HeldRenderer()).AtAsync(null!, TimeSpan.Zero, Cancel))).ParamName);

    private static Scrubber Scrubber(IThumbnailRenderer renderer, params Recording[] held)
    {
        var recordings = new HeldRecordings();
        recordings.Recordings.AddRange(held);

        return new Scrubber(
            recordings,
            renderer,
            new IntegritySettings { OutputRoots = [new StorageRootPath(Bulk, "/mounted/bulk")] });
    }

    private static Recording Begin(ServiceId service, OutputRoot? root = null)
    {
        RecordingId id = RecordingId.New();

        return Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(32737), service, new EventId(4098), Noon),
            root ?? Bulk,
            RecordingFileName.For(id, ".m2ts"),
            Noon,
            Noon.AddHours(1),
            new ProgrammeSnapshot("A programme", "What it is about", string.Empty, [], Noon),
            null,
            BroadcastGroupRole.Standalone,
            Noon);
    }

    private static Recording Ended(ServiceId service, OutputRoot? root = null)
    {
        Recording recording = Begin(service, root);

        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Abort(Noon.AddMinutes(30));
        recording.Settle(RecordingOutcome.Complete, 1_000, Noon.AddMinutes(30));

        return recording;
    }
}
