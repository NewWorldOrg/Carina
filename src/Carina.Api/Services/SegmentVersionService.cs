using Carina.Api.Common;
using Carina.Domain.Segments;

namespace Carina.Api.Services;

public enum SegmentVersionFailure
{
    Unreadable = 1,

    NoSuchVersion = 3,
}

/// <summary>
/// Chooses the version of the model in use by hand, or leaves the choice to learning again. Either goes through the
/// one change that moves the version in use; once it has moved, detection in spare time detects the recordings still
/// kept again with it.
/// </summary>
public sealed class SegmentVersionService(ISegmentModelVersionRepository versions, TimeProvider clock)
{
    public async Task<ServiceResult<SegmentModelVersion, SegmentVersionFailure>> ChooseAsync(
        int? number,
        bool? byHand,
        CancellationToken cancellationToken)
    {
        if (Change(number, byHand) is not { } change)
        {
            return ServiceResult<SegmentModelVersion, SegmentVersionFailure>.Failure(
                "number: expected the number of the version to choose by hand, or byHand: false to leave the choice to learning again; "
                + "a change names one of the two, not both.",
                SegmentVersionFailure.Unreadable);
        }

        return await versions.ChangeAsync(change, clock.GetUtcNow().UtcDateTime, cancellationToken) is { } active
            ? ServiceResult<SegmentModelVersion, SegmentVersionFailure>.Success(active)
            : ServiceResult<SegmentModelVersion, SegmentVersionFailure>.Failure(
                "No version of the model has that number.",
                SegmentVersionFailure.NoSuchVersion);
    }

    private static SegmentVersionChange? Change(int? number, bool? byHand)
        => (number, byHand) switch
        {
            (int chosen, null) => SegmentVersionChange.ChosenByHand(chosen),
            (null, false) => SegmentVersionChange.LeftToLearning,
            _ => null,
        };
}
