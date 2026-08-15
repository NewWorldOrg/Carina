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

    public bool IsInRotation => RotationState is not RotationState.NeedsAttention;

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

    public static CandidateChannel Rehydrate(
        CandidateChannelId id,
        NetworkId networkId,
        ServiceId serviceId,
        TuningParameters tuning,
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
        DateTime lastSeenAt)
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
        };
    }

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

        LastMeasurement = measurement;
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
}
