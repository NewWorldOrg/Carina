using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Recordings;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Recordings;

internal sealed class HeldRecordings : IRecordingRepository
{
    private int listings;

    public List<Recording> Rows { get; } = [];

    public List<RecordingId> Saved { get; } = [];

    public Exception? Refusing { get; set; }

    public Exception? RefusingToAdd { get; set; }

    public bool RefusingToAddOnce { get; set; }

    public int Listings => Volatile.Read(ref listings);

    public Task<Recording?> FindAsync(RecordingId id, CancellationToken cancellationToken)
        => Task.FromResult(Rows.FirstOrDefault(row => row.Id.Equals(id)));

    public Task<IReadOnlyList<Recording>> ListInFlightAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref listings);

        return Refusing is { } refusal
            ? Task.FromException<IReadOnlyList<Recording>>(refusal)
            : Task.FromResult<IReadOnlyList<Recording>>(
                [.. Rows.Where(row => row.IsInFlight).OrderBy(row => row.ExpectedWindowEnd)]);
    }

    public Task<IReadOnlyList<Recording>> ListForReservationAsync(
        ReservationId reservationId,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Recording>>(
            [.. Rows.Where(row => reservationId.Equals(row.ReservationId))]);

    public Task AddAsync(Recording recording, CancellationToken cancellationToken)
    {
        if (RefusingToAdd is { } refusal)
        {
            if (RefusingToAddOnce)
            {
                RefusingToAdd = null;
            }

            return Task.FromException(refusal);
        }

        Rows.Add(recording);

        return Task.CompletedTask;
    }

    public Task SaveAsync(Recording recording, CancellationToken cancellationToken)
    {
        Saved.Add(recording.Id);

        return Task.CompletedTask;
    }
}

internal sealed class PlannedReservations : IReservationRecordingContract
{
    private readonly List<RecordingTick> due = [];

    public HashSet<Guid> Unclaimable { get; } = [];

    public bool DueOnlyOnce { get; set; }

    public List<ReservationId> Claimed { get; } = [];

    public List<ReservationId> Released { get; } = [];

    public PlannedReservations Holding(params RecordingTick[] ticks)
    {
        due.AddRange(ticks);

        return this;
    }

    public Task<IReadOnlyList<RecordingTick>> DueAtAsync(DateTime at, CancellationToken cancellationToken)
    {
        lock (due)
        {
            IReadOnlyList<RecordingTick> answering = [.. due];

            if (DueOnlyOnce)
            {
                due.Clear();
            }

            return Task.FromResult(answering);
        }
    }

    public Task<bool> ClaimAsync(ReservationId id, DateTime at, CancellationToken cancellationToken)
    {
        if (Unclaimable.Contains(id.Value))
        {
            return Task.FromResult(false);
        }

        Claimed.Add(id);

        return Task.FromResult(true);
    }

    public Task<bool> ReleaseAsync(ReservationId id, DateTime claimedAt, CancellationToken cancellationToken)
    {
        Released.Add(id);

        return Task.FromResult(true);
    }
}

internal sealed class HeldMoment(DateTime now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal static class RecordingTickFixture
{
    public static readonly DateTime Airs = new(2026, 8, 26, 20, 0, 0, DateTimeKind.Utc);

    public static readonly TimeSpan Head = TimeSpan.FromSeconds(15);

    public static readonly RecordingSettings Settings = new(
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(5),
        Head,
        new OutputRoot("primary"));

    public static readonly TuningResolution Terrestrial = TuningResolution.Tunable(
        new CandidateChannelId(Guid.NewGuid()),
        TuningParameters.Terrestrial(27),
        impaired: false);

    public static RecordingTick Due(
        int eventId,
        DateTime? from = null,
        DateTime? until = null,
        DateTime? startedAt = null)
        => new(
            ReservationId.New(),
            new NetworkId(32736),
            new ServiceId(1024),
            new EventId(eventId),
            Airs,
            new ProgrammeSnapshot(
                "A programme",
                "What it is about",
                "Every detail of it",
                [new ProgrammeGenre(7, 1)],
                Airs.AddHours(-6)),
            Priority.Default,
            null,
            BroadcastGroupRole.Standalone,
            from ?? Airs,
            until ?? Airs.AddMinutes(30),
            true,
            startedAt);

    public static Recording InFlight(DateTime from, DateTime until, string deviceId = "adapter1")
    {
        RecordingId id = RecordingId.New();

        return Recording.Begin(
            id,
            ReservationId.New(),
            new ProgrammeRef(new NetworkId(32736), new ServiceId(1025), new EventId(9), Airs),
            new OutputRoot("primary"),
            RecordingFileName.For(id, ".ts"),
            from,
            until,
            new ProgrammeSnapshot("Another programme", string.Empty, string.Empty, [], Airs.AddHours(-6)),
            null,
            BroadcastGroupRole.Standalone,
            from,
            new TunerDeviceId(deviceId));
    }
}
