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

    public List<SegmentModelVersion> Written { get; } = [];

    private SegmentModelVersion[] Kept => [Active, .. Others];

    public static SegmentModelVersion InUse(int number, DateTime adoptedAt)
        => SegmentModelVersion.Rehydrate(number, SegmentModelVersionState.Active, false, SegmentModel.First, adoptedAt, adoptedAt, null, null, null);

    public Task<IReadOnlyList<SegmentModelVersion>> ListAsync(CancellationToken cancellationToken)
    {
        Reads++;

        return Task.FromResult<IReadOnlyList<SegmentModelVersion>>([.. Kept.OrderByDescending(version => version.Number)]);
    }

    public Task<SegmentModelVersion?> ChangeAsync(SegmentVersionChange change, DateTime at, CancellationToken cancellationToken)
    {
        if (SegmentModelVersion.Change(Kept, change, at) is not { } changed)
        {
            return Task.FromResult<SegmentModelVersion?>(null);
        }

        SegmentModelVersion[] after = [.. Kept.Select(version => changed.Written.FirstOrDefault(written => written.Number == version.Number) ?? version)];

        Active = changed.Active;
        Others.Clear();
        Others.AddRange(after.Where(version => version.Number != changed.Active.Number));
        Written.AddRange(changed.Written);

        return Task.FromResult<SegmentModelVersion?>(changed.Active);
    }

    public Task<SegmentModelVersion> ActiveAsync(CancellationToken cancellationToken)
    {
        Reads++;

        return Task.FromResult(Active);
    }
}
