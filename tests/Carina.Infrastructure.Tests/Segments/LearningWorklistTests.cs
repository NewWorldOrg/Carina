using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
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

    [Fact(DisplayName = "without the guide a programme ends when its reservation says, once that end has been announced")]
    public async Task WithoutTheGuideAProgrammeEndsWhenItsReservationSays()
    {
        Reservation confirmed = ReservationFixtures.Planned(endAt: Now.AddMinutes(30), endAtConfirmed: true);
        Reservation provisional = ReservationFixtures.Planned(endAt: Now.AddMinutes(30), endAtConfirmed: false);
        await reservations.AddAsync(confirmed, Cancel);
        await reservations.AddAsync(provisional, Cancel);

        Assert.Equal(Now.AddMinutes(30), await Worklist.ProgrammeEndsAtAsync(Made(Mounted, 7407, Now, confirmed.Id), Cancel));
        Assert.Null(await Worklist.ProgrammeEndsAtAsync(Made(Mounted, 7408, Now, provisional.Id), Cancel));
        Assert.Null(await Worklist.ProgrammeEndsAtAsync(Made(Mounted, 7409, Now), Cancel));
    }

    [Fact(DisplayName = "an end no later than the programme's start is no end")]
    public async Task AnEndNoLaterThanTheStartIsNoEnd()
    {
        Recording recording = Made(Mounted, 7410, Now);
        await programmes.AddAsync(Announced(recording, recording.ProgrammeStartsAt), Cancel);

        Assert.Null(await Worklist.ProgrammeEndsAtAsync(recording, Cancel));
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
