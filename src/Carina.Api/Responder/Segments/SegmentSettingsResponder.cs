using Carina.Domain.Segments;

namespace Carina.Api.Responder.Segments;

/// <summary>
/// The segment settings: whether CM, OP and ED learning is on, and the threshold a skip button is offered from,
/// each with when it last changed. A time is null until somebody changes that setting, and the setting then
/// stands at its default: learning off, the threshold at 0.70.
/// </summary>
public sealed record SegmentSettingsResponder(bool Learning, DateTime? LearningChangedAt, double SkipThreshold, DateTime? SkipThresholdChangedAt)
{
    public static SegmentSettingsResponder Of(SegmentSettingsStanding standing)
    {
        ArgumentNullException.ThrowIfNull(standing);

        return new SegmentSettingsResponder(standing.Learning, standing.LearningChangedAt, standing.SkipThreshold, standing.SkipThresholdChangedAt);
    }
}
