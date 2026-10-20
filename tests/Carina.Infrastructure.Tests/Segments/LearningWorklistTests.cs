using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Segments;
using Carina.Infrastructure.Segments;
using Carina.Infrastructure.Tests.Reservations;
using Carina.TestSupport;

using static Carina.Infrastructure.Tests.Segments.LearningFollowHarness;

namespace Carina.Infrastructure.Tests.Segments;

public sealed class LearningWorklistTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly KeptRecordings recordings = new();

    private readonly HeldProgrammes programmes = new();

    private readonly HeldReservations reservations = new();

    private LearningWorklist Worklist => new(recordings, programmes, reservations);

    [Fact(DisplayName = "the recordings being written are those under the roots named, the earliest started first")]
    public async Task TheRecordingsBeingWrittenAreThoseUnderTheRootsNamedEarliestFirst()
    {
        Recording later = await HeldAsync(Made(Mounted, 7401, Now.AddMinutes(-1)));
        Recording earlier = await HeldAsync(Made(Mounted, 7402, Now.AddMinutes(-9)));
        await HeldAsync(Made(Unmounted, 7403, Now.AddMinutes(-5)));

        IReadOnlyList<Recording> being = await Worklist.BeingRecordedAsync([Mounted], Cancel);

        Assert.Equal([earlier.Id, later.Id], being.Select(recording => recording.Id));
        Assert.Empty(await Worklist.BeingRecordedAsync([], Cancel));
    }

    [Fact(DisplayName = "a recording is being written, has ended, or is not kept at all")]
    public async Task ARecordingIsBeingWrittenHasEndedOrIsNotKept()
    {
        Recording writing = await HeldAsync(Made(Mounted, 7404, Now));
        Recording ended = await HeldAsync(Made(Mounted, 7405, Now));
        End(ended);

        Assert.Equal(RecordingStanding.InFlight, await Worklist.StandingAsync(writing.Id, Cancel));
        Assert.Equal(RecordingStanding.Ended, await Worklist.StandingAsync(ended.Id, Cancel));
        Assert.Null(await Worklist.StandingAsync(RecordingId.New(), Cancel));
    }

    [Fact(DisplayName = "a programme ends when the guide says it does")]
    public async Task AProgrammeEndsWhenTheGuideSays()
    {
        Recording recording = Made(Mounted, 7406, Now);
        await programmes.AddAsync(Announced(recording, Now.AddMinutes(25)), Cancel);

        Assert.Equal(Now.AddMinutes(25), await Worklist.ProgrammeEndsAtAsync(recording, Cancel));
    }

    [Fact(DisplayName = "without the guide a programme ends when its reservation says, once that end has been announced, and otherwise where its recording was promised to end")]
    public async Task WithoutTheGuideAProgrammeEndsWhenItsReservationSays()
    {
        Reservation confirmed = ReservationFixtures.Planned(endAt: Now.AddMinutes(30), endAtConfirmed: true);
        Reservation provisional = ReservationFixtures.Planned(endAt: Now.AddMinutes(30), endAtConfirmed: false);
        await reservations.AddAsync(confirmed, Cancel);
        await reservations.AddAsync(provisional, Cancel);
        Recording unreserved = Made(Mounted, 7409, Now);

        Assert.Equal(Now.AddMinutes(30), await Worklist.ProgrammeEndsAtAsync(Made(Mounted, 7407, Now, confirmed.Id), Cancel));
        Assert.Equal(Now.AddMinutes(54), await Worklist.ProgrammeEndsAtAsync(Made(Mounted, 7408, Now, provisional.Id), Cancel));
        Assert.Equal(unreserved.PromisedWindowEnd, await Worklist.ProgrammeEndsAtAsync(unreserved, Cancel));
        Assert.Equal(Now.AddMinutes(54), unreserved.PromisedWindowEnd);
    }

    [Fact(DisplayName = "an end from the guide no later than the programme's start is passed over for where the recording was promised to end")]
    public async Task AnEndNoLaterThanTheStartIsPassedOver()
    {
        Recording recording = Made(Mounted, 7410, Now);
        await programmes.AddAsync(Announced(recording, recording.ProgrammeStartsAt), Cancel);

        Assert.Equal(recording.PromisedWindowEnd, await Worklist.ProgrammeEndsAtAsync(recording, Cancel));
    }

    [Fact(DisplayName = "BR-CD-005: a sound heard at the end of two weekly recordings whose programmes the guide no longer holds is their ending, the programmes ending where the recordings were promised to")]
    public async Task AnEndingIsFoundWhereTheGuideNoLongerHoldsTheProgrammes()
    {
        Recording[] weeks = [Weekly(0), Weekly(1)];
        ProgrammeCopy[] copies = [.. await Task.WhenAll(weeks.Select(async week => ProgrammeCopy.Of(week, await Worklist.ProgrammeEndsAtAsync(week, Cancel))))];
        ProgrammeCopy[] unended = [.. weeks.Select(week => ProgrammeCopy.Of(week, null))];
        CatalogEntryId entry = CatalogEntryId.New();

        CatalogPattern ended = CatalogKinds.Estimate(TimeSpan.FromSeconds(89), [.. weeks.Select((week, at) => EndingOf(entry, week, copies[at]))], SeriesLinks.None);
        CatalogPattern unknown = CatalogKinds.Estimate(TimeSpan.FromSeconds(89), [.. weeks.Select((week, at) => EndingOf(entry, week, unended[at]))], SeriesLinks.None);

        Assert.Equal([.. weeks.Select(week => (DateTime?)week.PromisedWindowEnd)], copies.Select(copy => copy.ProgrammeEndsAt));
        Assert.Equal(new CatalogEstimate(CatalogKind.Ending, 0.8, CatalogDecision.Pattern), ended.Estimate);
        Assert.Equal(CatalogEstimate.Unknown, unknown.Estimate);
    }

    /// <summary>
    /// A weekly recording of a half-hour programme the guide holds no row for, started ten seconds before it and
    /// promised to end with it.
    /// </summary>
    private static Recording Weekly(int week)
    {
        RecordingId id = RecordingId.New();
        DateTime starts = Now.AddDays(7 * week);

        return Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(32736), new ServiceId(1040), new EventId(7420 + week), starts),
            Mounted,
            RecordingFileName.For(id, ".m2ts"),
            starts.AddSeconds(-10),
            starts.AddMinutes(30),
            new ProgrammeSnapshot("架空の物語", string.Empty, string.Empty, [], starts.AddDays(-1), AudioMode.Stereo, ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            starts.AddSeconds(-10),
            new TunerDeviceId("synthetic-0"));
    }

    private static PlacedOccurrence EndingOf(CatalogEntryId entry, Recording recording, ProgrammeCopy copy)
    {
        TimeSpan starts = recording.ProgrammeStartsAt.AddMinutes(30) - TimeSpan.FromSeconds(120) - recording.StartedAtActual;
        SoundSpan span = new(starts, starts + TimeSpan.FromSeconds(89));

        return PlacedOccurrence.Of(CatalogOccurrence.Found(entry, recording.Id, span, copy, 0.05, Now), copy);
    }

    private async Task<Recording> HeldAsync(Recording recording)
    {
        await recordings.AddAsync(recording, Cancel);

        return recording;
    }

    private static Programme Announced(Recording recording, DateTime endsAt)
        => Programme.Rehydrate(
            new ProgrammeId(recording.NetworkId, recording.ServiceId, recording.EventId),
            new TransportStreamId(32736),
            recording.ProgrammeStartsAt,
            endsAt,
            "A programme",
            string.Empty,
            false,
            Now);

    private sealed class KeptRecordings : IRecordingRepository
    {
        private readonly List<Recording> kept = [];

        public Task<Recording?> FindAsync(RecordingId id, CancellationToken cancellationToken)
            => Task.FromResult(kept.FirstOrDefault(recording => recording.Id.Equals(id)));

        public Task<IReadOnlyList<Recording>> ListInFlightAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Recording>>([.. kept.Where(recording => recording.IsInFlight)]);

        public Task<IReadOnlyList<Recording>> ListForReservationAsync(ReservationId reservationId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Recording>>([.. kept.Where(recording => reservationId.Equals(recording.ReservationId))]);

        public Task AddAsync(Recording recording, CancellationToken cancellationToken)
        {
            kept.Add(recording);

            return Task.CompletedTask;
        }

        public Task SaveAsync(Recording recording, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
