using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Recordings;

using Puppis.Contracts;

namespace Carina.Infrastructure.Tests.Recordings;

public sealed class InFlightRecordingTitlesTests
{
    private static readonly DateTime Noon = new(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ARecordingBeingWrittenIsNamedBySessionItIsWrittenIn()
    {
        var recordings = new Recordings();
        Recording writing = Begun("Example Programme");
        Recording ended = Begun("Ended Programme");
        ended.Wrote(TimeSpan.FromMinutes(1));
        ended.Abort(Noon.AddMinutes(1));
        ended.Settle(RecordingOutcome.Complete, 1, Noon.AddMinutes(1));
        recordings.Kept.AddRange([writing, ended]);

        IReadOnlyDictionary<SessionId, string> titles =
            await new InFlightRecordingTitles(recordings).TitlesAsync(CancellationToken.None);

        Assert.Equal("Example Programme", Assert.Single(titles).Value);
        Assert.Equal(RecordingSessions.Named(writing.Id), titles.Keys.Single());
    }

    private static Recording Begun(string name)
    {
        RecordingId id = RecordingId.New();

        return Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(50001), new ServiceId(1040), new EventId(4001), Noon),
            new OutputRoot("primary"),
            RecordingFileName.For(id, ".m2ts"),
            Noon,
            Noon.AddMinutes(30),
            new ProgrammeSnapshot(
                name,
                string.Empty,
                string.Empty,
                [],
                Noon,
                AudioMode.Undetermined,
                ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            Noon,
            new TunerDeviceId("synthetic-0"));
    }

    private sealed class Recordings : IRecordingRepository
    {
        public List<Recording> Kept { get; } = [];

        public Task<Recording?> FindAsync(RecordingId id, CancellationToken cancellationToken)
            => Task.FromResult(Kept.FirstOrDefault(recording => recording.Id.Equals(id)));

        public Task<IReadOnlyList<Recording>> ListInFlightAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Recording>>([.. Kept.Where(recording => recording.IsInFlight)]);

        public Task<IReadOnlyList<Recording>> ListForReservationAsync(
            ReservationId reservationId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Recording>>([]);

        public Task AddAsync(Recording recording, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveAsync(Recording recording, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
