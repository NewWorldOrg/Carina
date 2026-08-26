using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Rules;

namespace Carina.Domain.Reservations;

public sealed class Reservation
{
    public const int NameMaxLength = 512;

    public const int SummaryMaxLength = 4096;

    public const int ExtendedMaxLength = 65536;

    private Reservation()
    {
    }

    public ReservationId Id { get; private set; } = null!;

    public NetworkId NetworkId { get; private set; } = null!;

    public ServiceId ServiceId { get; private set; } = null!;

    public EventId EventId { get; private set; } = null!;

    public DateTime ProgrammeStartsAt { get; private set; }

    public RuleId? RuleId { get; private set; }

    public Priority Priority { get; private set; } = null!;

    public DateTime StartAt { get; private set; }

    public DateTime EndAt { get; private set; }

    public bool EndAtConfirmed { get; private set; }

    public Margin MarginBefore { get; private set; } = null!;

    public Margin MarginAfter { get; private set; } = null!;

    public string SnapshotName { get; private set; } = string.Empty;

    public string SnapshotSummary { get; private set; } = string.Empty;

    public string SnapshotExtended { get; private set; } = string.Empty;

    public IReadOnlyList<ProgrammeGenre> SnapshotGenres { get; private set; } = [];

    public DateTime CapturedAt { get; private set; }

    public bool EpgDiverged { get; private set; }

    public IReadOnlyList<EpgDivergence> EpgDivergences { get; private set; } = [];

    public bool EpgMissing { get; private set; }

    public DateTime? AcknowledgedAt { get; private set; }

    public bool ReceptionUnavailable { get; private set; }

    public DateTime? ReceptionUnavailableSince { get; private set; }

    public BroadcastGroupKey? BroadcastGroupKey { get; private set; }

    public BroadcastGroupRole BroadcastGroupRole { get; private set; }

    public ReservationState State { get; private set; }

    public DateTime? StartedAt { get; private set; }

    public RecordingOutcome? RecordingOutcome { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public ProgrammeRef Programme => new(NetworkId, ServiceId, EventId, ProgrammeStartsAt);

    public DateTime EffectiveStartAt => StartAt - MarginBefore.Value;

    public DateTime EffectiveEndAt => EndAt + MarginAfter.Value;

    public bool IsPinned => StartedAt is not null;

    public bool IsRuleBorn => RuleId is not null;

    public static Reservation Plan(
        ReservationId id,
        ProgrammeRef programme,
        RuleId? ruleId,
        Priority priority,
        DateTime startAt,
        DateTime endAt,
        bool endAtConfirmed,
        Margin marginBefore,
        Margin marginAfter,
        ProgrammeSnapshot snapshot,
        BroadcastGroupKey? broadcastGroupKey,
        BroadcastGroupRole broadcastGroupRole,
        DateTime at)
        => Rehydrate(
            id,
            programme,
            ruleId,
            priority,
            startAt,
            endAt,
            endAtConfirmed,
            marginBefore,
            marginAfter,
            snapshot,
            broadcastGroupKey,
            broadcastGroupRole,
            ReservationState.Scheduled,
            null,
            null,
            false,
            [],
            false,
            null,
            false,
            null,
            at);

    public static Reservation Rehydrate(
        ReservationId id,
        ProgrammeRef programme,
        RuleId? ruleId,
        Priority priority,
        DateTime startAt,
        DateTime endAt,
        bool endAtConfirmed,
        Margin marginBefore,
        Margin marginAfter,
        ProgrammeSnapshot snapshot,
        BroadcastGroupKey? broadcastGroupKey,
        BroadcastGroupRole broadcastGroupRole,
        ReservationState state,
        DateTime? startedAt,
        RecordingOutcome? recordingOutcome,
        bool epgDiverged,
        IReadOnlyList<EpgDivergence> epgDivergences,
        bool epgMissing,
        DateTime? acknowledgedAt,
        bool receptionUnavailable,
        DateTime? receptionUnavailableSince,
        DateTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(programme);
        ArgumentNullException.ThrowIfNull(priority);
        ArgumentNullException.ThrowIfNull(marginBefore);
        ArgumentNullException.ThrowIfNull(marginAfter);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(epgDivergences);

        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "A reservation is in one of the four states it owns.");
        }

        if (!Enum.IsDefined(broadcastGroupRole))
        {
            throw new ArgumentOutOfRangeException(nameof(broadcastGroupRole), broadcastGroupRole, "A reservation names a role it can hold.");
        }

        if (endAt <= startAt)
        {
            throw new ArgumentException("A reservation ends after it starts.", nameof(endAt));
        }

        if (recordingOutcome is not null && startedAt is null)
        {
            throw new ArgumentException(
                "A recording outcome belongs to a reservation that was claimed.",
                nameof(recordingOutcome));
        }

        if (epgDiverged != (epgDivergences.Count > 0))
        {
            throw new ArgumentException(
                "A diverged reservation says what diverged, and one that has not diverged says nothing.",
                nameof(epgDivergences));
        }

        if (acknowledgedAt is not null && !epgDiverged && !epgMissing)
        {
            throw new ArgumentException(
                "Only a divergence or a disappearance is acknowledged.",
                nameof(acknowledgedAt));
        }

        if (receptionUnavailable != (receptionUnavailableSince is not null))
        {
            throw new ArgumentException(
                "A reservation with nowhere to tune says when that was noticed, and one with somewhere says nothing.",
                nameof(receptionUnavailableSince));
        }

        if (broadcastGroupRole is not BroadcastGroupRole.Standalone && broadcastGroupKey is null)
        {
            throw new ArgumentException(
                $"A reservation in the {broadcastGroupRole} role names the broadcast it belongs to.",
                nameof(broadcastGroupKey));
        }

        return new Reservation
        {
            Id = id,
            NetworkId = programme.NetworkId,
            ServiceId = programme.ServiceId,
            EventId = programme.EventId,
            ProgrammeStartsAt = programme.StartsAt,
            RuleId = ruleId,
            Priority = priority,
            StartAt = UtcTimes.Required(startAt, nameof(startAt)),
            EndAt = UtcTimes.Required(endAt, nameof(endAt)),
            EndAtConfirmed = endAtConfirmed,
            MarginBefore = marginBefore,
            MarginAfter = marginAfter,
            SnapshotName = snapshot.Name,
            SnapshotSummary = snapshot.Summary,
            SnapshotExtended = snapshot.Extended,
            SnapshotGenres = snapshot.Genres,
            CapturedAt = snapshot.CapturedAt,
            BroadcastGroupKey = broadcastGroupKey,
            BroadcastGroupRole = broadcastGroupRole,
            State = state,
            StartedAt = UtcTimes.Optional(startedAt, nameof(startedAt)),
            RecordingOutcome = recordingOutcome,
            EpgDiverged = epgDiverged,
            EpgDivergences = epgDivergences,
            EpgMissing = epgMissing,
            AcknowledgedAt = UtcTimes.Optional(acknowledgedAt, nameof(acknowledgedAt)),
            ReceptionUnavailable = receptionUnavailable,
            ReceptionUnavailableSince =
                UtcTimes.Optional(receptionUnavailableSince, nameof(receptionUnavailableSince)),
            CreatedAt = UtcTimes.Required(createdAt, nameof(createdAt)),
        };
    }

    public void Secure()
    {
        RefuseUnless(State is ReservationState.Scheduled or ReservationState.Conflict);

        State = ReservationState.Scheduled;
    }

    public void Contend()
    {
        RefuseUnless(State is ReservationState.Scheduled or ReservationState.Conflict);

        if (IsPinned)
        {
            throw new InvalidOperationException(
                "A reservation that has been claimed keeps the capacity it is already using.");
        }

        State = ReservationState.Conflict;
    }

    public void Cancel()
    {
        RefuseUnless(State is ReservationState.Scheduled or ReservationState.Conflict);

        State = ReservationState.Cancelled;
    }

    public void Restore()
    {
        RefuseUnless(State is ReservationState.Cancelled);

        State = ReservationState.Scheduled;
    }

    public void Miss()
    {
        RefuseUnless(State is ReservationState.Scheduled or ReservationState.Conflict);

        if (IsPinned)
        {
            throw new InvalidOperationException("A reservation that has been claimed was not missed.");
        }

        State = ReservationState.Missed;
    }

    public void Reprioritise(Priority priority)
    {
        ArgumentNullException.ThrowIfNull(priority);

        Priority = priority;
    }

    public void Reframe(DateTime startAt, DateTime endAt, bool endAtConfirmed)
    {
        if (endAt <= startAt)
        {
            throw new ArgumentException("A reservation ends after it starts.", nameof(endAt));
        }

        StartAt = UtcTimes.Required(startAt, nameof(startAt));
        EndAt = UtcTimes.Required(endAt, nameof(endAt));
        EndAtConfirmed = endAtConfirmed;
    }

    public void Diverge(IReadOnlyList<EpgDivergence> divergences)
    {
        ArgumentNullException.ThrowIfNull(divergences);

        if (divergences.Count is 0)
        {
            throw new ArgumentException("A divergence says what diverged.", nameof(divergences));
        }

        EpgDiverged = true;
        EpgDivergences = divergences;
        AcknowledgedAt = null;
    }

    public void Disappear()
    {
        EpgMissing = true;
        AcknowledgedAt = null;
    }

    public void LoseReception(DateTime at)
    {
        ReceptionUnavailable = true;
        ReceptionUnavailableSince = UtcTimes.Required(at, nameof(at));
    }

    public void RegainReception()
    {
        ReceptionUnavailable = false;
        ReceptionUnavailableSince = null;
    }

    public void Acknowledge(DateTime at)
    {
        if (!EpgDiverged && !EpgMissing)
        {
            throw new InvalidOperationException("There is nothing to acknowledge on this reservation.");
        }

        AcknowledgedAt = UtcTimes.Required(at, nameof(at));
    }

    public void Regroup(BroadcastGroupKey? key, BroadcastGroupRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "A reservation names a role it can hold.");
        }

        if (role is not BroadcastGroupRole.Standalone && key is null)
        {
            throw new ArgumentException(
                $"A reservation in the {role} role names the broadcast it belongs to.",
                nameof(key));
        }

        BroadcastGroupKey = key;
        BroadcastGroupRole = role;
    }

    private void RefuseUnless(bool allowed)
    {
        if (!allowed)
        {
            throw new InvalidOperationException($"A reservation in {State} does not make that move.");
        }
    }
}
