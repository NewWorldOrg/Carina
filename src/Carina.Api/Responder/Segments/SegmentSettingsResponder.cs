using Carina.Domain.Segments;

namespace Carina.Api.Responder.Segments;

public sealed record SegmentSettingsResponder(double SkipThreshold, DateTime? SkipThresholdChangedAt)
{
    public static SegmentSettingsResponder Of(SegmentSettingsStanding standing)
    {
        ArgumentNullException.ThrowIfNull(standing);

        return new SegmentSettingsResponder(standing.SkipThreshold, standing.SkipThresholdChangedAt);
    }
}
