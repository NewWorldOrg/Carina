using Carina.Domain.Segments;

namespace Carina.TestSupport;

/// <summary>
/// The version of the model in use, as a test sets it: the first, put in use at <see cref="Installed"/>,
/// until a test puts another in its place; and the versions used before it, as a test adds them. The built-in
/// version is taken as kept already: asking for it is kept and writes nothing, or fails as a test sets it. A
/// candidate is numbered after every number held so far and <see cref="RememberedElsewhere"/>, and letting go of
/// candidates leaves those a test says something still names (<see cref="Named"/>).
/// </summary>
public sealed class HeldSegmentModelVersions : ISegmentModelVersionRepository
{
    public static readonly DateTime Installed = new(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc);

    public SegmentModelVersion Active { get; set; } = InUse(SegmentModelVersion.FirstNumber, Installed);

    public List<SegmentModelVersion> Others { get; } = [];

    public int Reads { get; private set; }

    public List<SegmentModelVersion> Written { get; } = [];

    public List<SegmentModelVersion> Added { get; } = [];

    public List<(SegmentModel Model, DateTime At)> BuiltInAsked { get; } = [];

    public Exception? BuiltInFailure { get; set; }

    public int RememberedElsewhere { get; set; }

    public HashSet<int> Named { get; } = [];

    public List<int> LetGo { get; } = [];

    private SegmentModelVersion[] Kept => [Active, .. Others];

    private int HighestGiven => new[] { RememberedElsewhere, LetGo.DefaultIfEmpty().Max(), Kept.Max(version => version.Number) }.Max();

    public static SegmentModelVersion InUse(int number, DateTime adoptedAt)
        => SegmentModelVersion.Rehydrate(number, SegmentModelVersionState.Active, false, HandMadeModels.First, adoptedAt, adoptedAt, null, null, null);

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

    public Task<SegmentModelVersion> AddCandidateAsync(SegmentModel model, SegmentEvaluation evaluation, DateTime trainedAt, CancellationToken cancellationToken)
    {
        SegmentModelVersion candidate = SegmentModelVersion.Learned(SegmentModelVersion.NumberAfter(HighestGiven), model, evaluation, trainedAt);

        Others.Add(candidate);
        Added.Add(candidate);

        return Task.FromResult(candidate);
    }

    public Task<IReadOnlyList<int>> LetGoOfCandidatesBeforeAsync(int latest, CancellationToken cancellationToken)
    {
        int[] letGo = [.. SegmentModelVersion.LetGoBefore(Kept, latest).Where(number => !Named.Contains(number))];

        Others.RemoveAll(version => letGo.Contains(version.Number));
        LetGo.AddRange(letGo);

        return Task.FromResult<IReadOnlyList<int>>(letGo);
    }

    public Task<SegmentBuiltInKept?> KeepBuiltInAsync(SegmentModel builtIn, DateTime at, CancellationToken cancellationToken)
    {
        BuiltInAsked.Add((builtIn, at));

        return BuiltInFailure is { } failure
            ? Task.FromException<SegmentBuiltInKept?>(failure)
            : Task.FromResult<SegmentBuiltInKept?>(null);
    }

    public Task<SegmentModelVersion> ActiveAsync(CancellationToken cancellationToken)
    {
        Reads++;

        return Task.FromResult(Active);
    }

    public Task<SegmentModelVersion?> FindAsync(int number, CancellationToken cancellationToken)
        => Task.FromResult(Active.Number == number ? Active : Others.FirstOrDefault(version => version.Number == number));
}
