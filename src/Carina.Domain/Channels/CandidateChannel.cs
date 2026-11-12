using Carina.Domain.Base;

namespace Carina.Domain.Channels;

public sealed class CandidateChannel
{
    private CandidateChannel()
    {
    }

    public CandidateChannelId Id { get; private set; } = null!;

    public NetworkId NetworkId { get; private set; } = null!;

    public ServiceId ServiceId { get; private set; } = null!;

    public TuningParameters Tuning { get; private set; } = null!;

    public TransportStreamId? ObservedStreamId { get; private set; }

    public bool IsSelected { get; private set; }

    public SelectionSource? SelectionSource { get; private set; }

    public DateTime? SelectedAt { get; private set; }

    public SignalMeasurement? SelectionMeasurement { get; private set; }

    public SignalMeasurement? LastMeasurement { get; private set; }

    public bool NeedsRevalidation { get; private set; }

    public RotationState RotationState { get; private set; }

    public int ConsecutiveFailures { get; private set; }

    public DateTime? NextAttemptAt { get; private set; }

    public DateTime? NeedsAttentionSince { get; private set; }

    public DateTime DiscoveredAt { get; private set; }

    public DateTime LastSeenAt { get; private set; }

    public CandidateScore? Score { get; private set; }

    public bool IsInRotation => RotationState is not RotationState.NeedsAttention;

    /// <summary>
    /// A candidate is a way to one service, so it is chosen or dropped only through that service.
    /// </summary>
    public bool BelongsTo(NetworkId networkId, ServiceId serviceId)
        => NetworkId.Equals(networkId) && ServiceId.Equals(serviceId);

    public static CandidateChannel Discover(
        CandidateChannelId id,
        NetworkId networkId,
        ServiceId serviceId,
        TuningParameters tuning,
        DateTime at)
        => Rehydrate(
            id,
            networkId,
            serviceId,
            tuning,
            observedStreamId: null,
            isSelected: false,
            selectionSource: null,
            selectedAt: null,
            selectionMeasurement: null,
            lastMeasurement: null,
            needsRevalidation: false,
            rotationState: RotationState.Active,
            consecutiveFailures: 0,
            nextAttemptAt: null,
            needsAttentionSince: null,
            discoveredAt: at,
            lastSeenAt: at);

    /// <summary>
    /// A candidate added by hand has not been tuned by anything here, so it waits to be checked before it is trusted.
    /// </summary>
    public static CandidateChannel AddByHand(
        CandidateChannelId id,
        NetworkId networkId,
        ServiceId serviceId,
        TuningParameters tuning,
        DateTime at)
    {
        CandidateChannel added = Discover(id, networkId, serviceId, tuning, at);

        added.RequireRevalidation();

        return added;
    }

    /// <summary>
    /// A candidate a scan found starts with the stream it saw, and as received when the scan measured it.
    /// </summary>
    public static CandidateChannel FoundByScan(
        CandidateChannelId id,
        NetworkId networkId,
        ServiceId serviceId,
        TuningParameters tuning,
        TransportStreamId? observedStreamId,
        SignalMeasurement? measurement,
        DateTime at)
    {
        CandidateChannel found = Discover(id, networkId, serviceId, tuning, at);

        found.CarriedBy(observedStreamId);

        if (measurement is not null)
        {
            found.RecordTuningSuccess(measurement, at);
        }

        return found;
    }

    public static CandidateChannel Rehydrate(
        CandidateChannelId id,
        NetworkId networkId,
        ServiceId serviceId,
        TuningParameters tuning,
        TransportStreamId? observedStreamId,
        bool isSelected,
        SelectionSource? selectionSource,
        DateTime? selectedAt,
        SignalMeasurement? selectionMeasurement,
        SignalMeasurement? lastMeasurement,
        bool needsRevalidation,
        RotationState rotationState,
        int consecutiveFailures,
        DateTime? nextAttemptAt,
        DateTime? needsAttentionSince,
        DateTime discoveredAt,
        DateTime lastSeenAt,
        CandidateScore? score = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(serviceId);
        ArgumentNullException.ThrowIfNull(tuning);
        ArgumentOutOfRangeException.ThrowIfNegative(consecutiveFailures);

        if (isSelected != (selectionSource is not null))
        {
            throw new ArgumentException(
                "A selected candidate names what selected it, and an unselected one names nothing.",
                nameof(selectionSource));
        }

        return new CandidateChannel
        {
            Id = id,
            NetworkId = networkId,
            ServiceId = serviceId,
            Tuning = tuning,
            ObservedStreamId = observedStreamId,
            IsSelected = isSelected,
            SelectionSource = selectionSource,
            SelectedAt = UtcTimes.Optional(selectedAt, nameof(selectedAt)),
            SelectionMeasurement = selectionMeasurement,
            LastMeasurement = lastMeasurement,
            NeedsRevalidation = needsRevalidation,
            RotationState = rotationState,
            ConsecutiveFailures = consecutiveFailures,
            NextAttemptAt = UtcTimes.Optional(nextAttemptAt, nameof(nextAttemptAt)),
            NeedsAttentionSince = UtcTimes.Optional(needsAttentionSince, nameof(needsAttentionSince)),
            DiscoveredAt = UtcTimes.Required(discoveredAt, nameof(discoveredAt)),
            LastSeenAt = UtcTimes.Required(lastSeenAt, nameof(lastSeenAt)),
            Score = score,
        };
    }

    public void Evaluated(CandidateScore score)
    {
        ArgumentNullException.ThrowIfNull(score);

        Score = score;
    }

    public void CarriedBy(TransportStreamId? observedStreamId)
    {
        if (observedStreamId is null)
        {
            return;
        }

        ObservedStreamId = observedStreamId;
    }

    /// <summary>
    /// Whether a stream seen on this candidate's channel is another than the one it is known to carry; a scan that saw
    /// no stream says nothing.
    /// </summary>
    public bool IsRestatedBy(TransportStreamId? observedStreamId)
        => observedStreamId is not null && !observedStreamId.Equals(ObservedStreamId);

    /// <summary>
    /// Takes the stream seen when it is another than the one known, and says whether it did.
    /// </summary>
    public bool Restate(TransportStreamId? observedStreamId)
    {
        if (!IsRestatedBy(observedStreamId))
        {
            return false;
        }

        ObservedStreamId = observedStreamId;

        return true;
    }

    /// <summary>
    /// Selected by a scan with nothing measured to compare it by, so a later scan may choose again.
    /// </summary>
    public bool ChosenBlindByAScan
        => IsSelected && SelectionSource is Channels.SelectionSource.Scan && SelectionMeasurement is null;

    public void Select(SelectionSource source, SignalMeasurement? measuredAtSelection, DateTime at)
    {
        IsSelected = true;
        SelectionSource = source;
        SelectedAt = UtcTimes.Required(at, nameof(at));
        SelectionMeasurement = measuredAtSelection;
    }

    public void Deselect()
    {
        IsSelected = false;
        SelectionSource = null;
        SelectedAt = null;
        SelectionMeasurement = null;
    }

    public void RecordTuningSuccess(SignalMeasurement measurement, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(measurement);

        LastMeasurement = TheReadingThatStands(LastMeasurement, measurement);
        LastSeenAt = UtcTimes.Required(at, nameof(at));
        NeedsRevalidation = false;
        RotationState = RotationState.Active;
        ConsecutiveFailures = 0;
        NextAttemptAt = null;
        NeedsAttentionSince = null;
    }

    public void RecordTuningFailure(RotationBackoff backoff, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(backoff);
        UtcTimes.Required(at, nameof(at));

        ConsecutiveFailures++;

        if (ConsecutiveFailures >= backoff.FailureCeiling)
        {
            RotationState = RotationState.NeedsAttention;
            NextAttemptAt = null;
            NeedsAttentionSince ??= at;

            return;
        }

        RotationState = RotationState.BackingOff;
        NextAttemptAt = at + backoff.DelayAfter(ConsecutiveFailures);
        NeedsAttentionSince = null;
    }

    public void ReturnToRotation(DateTime at)
    {
        UtcTimes.Required(at, nameof(at));

        RotationState = RotationState.Active;
        ConsecutiveFailures = 0;
        NextAttemptAt = null;
        NeedsAttentionSince = null;
    }

    public void RequireRevalidation()
    {
        NeedsRevalidation = true;
    }

    private static SignalMeasurement TheReadingThatStands(SignalMeasurement? held, SignalMeasurement arriving)
        => arriving is { Locked: true, CnrMilliDecibels: null }
           && held is { Locked: true, CnrMilliDecibels: not null }
            ? held
            : arriving;
}
