using Carina.Domain.Segments;

namespace Carina.Api.Responder.Segments;

/// <summary>
/// The segment settings: the threshold a skip button is offered from, with when it last changed. The time is null
/// until somebody changes the threshold, which then stands at its default of 0.70.
/// </summary>
public sealed record SegmentSettingsResponder(double SkipThreshold, DateTime? SkipThresholdChangedAt)
{
    public static SegmentSettingsResponder Of(SegmentSettingsStanding standing)
    {
        ArgumentNullException.ThrowIfNull(standing);

        return new SegmentSettingsResponder(standing.SkipThreshold, standing.SkipThresholdChangedAt);
    }
}
