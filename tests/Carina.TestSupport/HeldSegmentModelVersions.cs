using Carina.Domain.Segments;

namespace Carina.TestSupport;

/// <summary>
/// The version of the model in use, as a test sets it: the first, put in use at <see cref="Installed"/>,
/// until a test puts another in its place.
/// </summary>
public sealed class HeldSegmentModelVersions : ISegmentModelVersionRepository
{
    public static readonly DateTime Installed = new(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc);

    public SegmentModelVersion Active { get; set; } = InUse(SegmentModelVersion.FirstNumber, Installed);

    public int Reads { get; private set; }

    public static SegmentModelVersion InUse(int number, DateTime adoptedAt)
        => SegmentModelVersion.Rehydrate(number, SegmentModelVersionState.Active, false, SegmentModel.First, adoptedAt, adoptedAt, null, null, null);

    public Task<SegmentModelVersion> ActiveAsync(CancellationToken cancellationToken)
    {
        Reads++;

        return Task.FromResult(Active);
    }
}
