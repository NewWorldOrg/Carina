using Carina.Api.Responder.Scans;
using Carina.Api.Services;
using Carina.Domain.Channels;

namespace Carina.Api.Responder.Services;

public sealed record CandidateSelectionResponder(
    SelectionSource Source,
    DateTimeOffset SelectedAt,
    ScanMeasurementResponder? Measurement)
{
    public static CandidateSelectionResponder? Of(CandidateChannel candidate)
        => candidate.IsSelected
            ? new CandidateSelectionResponder(
                candidate.SelectionSource!.Value,
                candidate.SelectedAt!.Value,
                ScanMeasurementResponder.Of(candidate.SelectionMeasurement))
            : null;
}

public sealed record CandidateChannelResponder(
    Guid Id,
    ScanTargetResponder Target,
    bool IsSelected,
    CandidateSelectionResponder? Selection,
    ScanMeasurementResponder? LastMeasurement,
    bool NeedsRevalidation,
    RotationState RotationState,
    int ConsecutiveFailures,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset? NeedsAttentionSince,
    DateTimeOffset DiscoveredAt,
    DateTimeOffset LastSeenAt)
{
    public static CandidateChannelResponder Of(CandidateChannel candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        return new CandidateChannelResponder(
            candidate.Id.Value,
            ScanTargetResponder.Of(candidate.Tuning),
            candidate.IsSelected,
            CandidateSelectionResponder.Of(candidate),
            ScanMeasurementResponder.Of(candidate.LastMeasurement),
            candidate.NeedsRevalidation,
            candidate.RotationState,
            candidate.ConsecutiveFailures,
            candidate.NextAttemptAt,
            candidate.NeedsAttentionSince,
            candidate.DiscoveredAt,
            candidate.LastSeenAt);
    }
}

public sealed record BroadcastServiceResponder(
    int NetworkId,
    int ServiceId,
    string Name,
    ServiceCategory Category,
    bool ReservableByDefault,
    DateTimeOffset DiscoveredAt,
    DateTimeOffset LastSeenAt,
    int CandidateCount,
    ScanTargetResponder? SelectedChannel,
    IReadOnlyList<CandidateChannelResponder> Candidates)
{
    public static BroadcastServiceResponder Of(ServiceWithChannels held)
    {
        ArgumentNullException.ThrowIfNull(held);

        var service = held.Service;
        var selected = held.Candidates.FirstOrDefault(candidate => candidate.IsSelected);

        return new BroadcastServiceResponder(
            service.NetworkId.Value,
            service.ServiceId.Value,
            service.Name,
            service.Category,
            service.ReservableByDefault,
            service.DiscoveredAt,
            service.LastSeenAt,
            held.Candidates.Count,
            selected is null ? null : ScanTargetResponder.Of(selected.Tuning),
            [.. held.Candidates.Select(CandidateChannelResponder.Of)]);
    }
}
