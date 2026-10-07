using Carina.Domain.Segments;

namespace Carina.Api.Responder.Segments;

/// <summary>
/// The segment settings: whether CM, OP and ED learning is on, and when it last changed.
/// <c>learningChangedAt</c> is null until somebody changes it, and learning is then off.
/// </summary>
public sealed record SegmentSettingsResponder(bool Learning, DateTime? LearningChangedAt)
{
    public static SegmentSettingsResponder Of(SegmentSettingsStanding standing)
    {
        ArgumentNullException.ThrowIfNull(standing);

        return new SegmentSettingsResponder(standing.Learning, standing.LearningChangedAt);
    }
}
