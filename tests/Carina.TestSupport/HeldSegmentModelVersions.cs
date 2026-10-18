using Carina.Domain.Segments;

namespace Carina.TestSupport;

/// <summary>
/// The version of the model in use, as a test sets it: the first, put in use at <see cref="Installed"/>,
/// until a test puts another in its place; and the versions used before it, as a test adds them.
/// </summary>
public sealed class HeldSegmentModelVersions : ISegmentModelVersionRepository
{
    public static readonly DateTime Installed = new(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc);

    public SegmentModelVersion Active { get; set; } = InUse(SegmentModelVersion.FirstNumber, Installed);

    public List<SegmentModelVersion> Others { get; } = [];

    public int Reads { get; private set; }

    public static SegmentModelVersion InUse(int number, DateTime adoptedAt)
        => SegmentModelVersion.Rehydrate(number, SegmentModelVersionState.Active, false, SegmentModel.First, adoptedAt, adoptedAt, null, null, null);

    public Task<SegmentModelVersion> ActiveAsync(CancellationToken cancellationToken)
    {
        Reads++;

        return Task.FromResult(Active);
    }

    public Task<SegmentModelVersion?> FindAsync(int number, CancellationToken cancellationToken)
        => Task.FromResult(Active.Number == number ? Active : Others.FirstOrDefault(version => version.Number == number));
}
