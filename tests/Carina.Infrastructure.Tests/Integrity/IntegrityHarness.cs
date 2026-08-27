using Carina.Domain.Base;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;

namespace Carina.Infrastructure.Tests.Integrity;

internal sealed class StoppedClock(DateTime now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
}

internal sealed class HeldLedger(params LedgerFile[] rows) : IRecordingLedger
{
    private readonly List<LedgerFile> rows = [.. rows];

    public TaskCompletionSource? Gate { get; set; }

    public int Reads { get; private set; }

    public async Task<IReadOnlyList<LedgerFile>> ListAsync(CancellationToken cancellationToken)
    {
        Reads++;

        if (Gate is { } waiting)
        {
            await waiting.Task.WaitAsync(cancellationToken);
        }

        return [.. rows];
    }
}

internal sealed class HeldSurvey : IRecordingFileSurvey
{
    private readonly Dictionary<string, RootListing> listings = new(StringComparer.Ordinal);
    private readonly List<OutputRoot> roots = [];

    public List<string> Asked { get; } = [];

    public HeldSurvey Declaring(OutputRoot root, params (string Path, long SizeBytes)[] files)
    {
        roots.Add(root);
        listings[root.Value] = RootListing.Of(
            root,
            [.. files.Select(file => new StoredFile(file.Path, file.SizeBytes))]);

        return this;
    }

    public HeldSurvey DeclaringOutOfReach(OutputRoot root)
    {
        roots.Add(root);
        listings[root.Value] = RootListing.OutOfReach(root);

        return this;
    }

    public Task<IReadOnlyList<OutputRoot>> RootsAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<OutputRoot>>([.. roots]);

    public Task<RootListing> ListAsync(OutputRoot root, CancellationToken cancellationToken)
    {
        Asked.Add(root.Value);

        return Task.FromResult(
            listings.TryGetValue(root.Value, out RootListing? listing)
                ? listing
                : RootListing.OutOfReach(root));
    }
}

internal sealed class HeldChecks : IIntegrityCheckRepository
{
    public List<IntegrityReport> Saved { get; } = [];

    public Task SaveAsync(IntegrityReport report, CancellationToken cancellationToken)
    {
        Saved.Add(report);

        return Task.CompletedTask;
    }

    public Task<IntegrityCheck?> LatestAsync(CancellationToken cancellationToken)
        => Task.FromResult(Saved.Count is 0 ? null : Saved[^1].Check);

    public Task<PaginatedList<IntegrityFinding>> ListFindingsAsync(
        IntegrityCheckId checkId,
        IntegrityFindingQuery query,
        CancellationToken cancellationToken)
    {
        IntegrityFinding[] found =
        [
            .. Saved
                .Where(report => report.Check.Id.Equals(checkId))
                .SelectMany(report => report.Findings)
                .OrderBy(finding => finding.Root.Value, StringComparer.Ordinal)
                .ThenBy(finding => finding.Path, StringComparer.Ordinal),
        ];

        return Task.FromResult(new PaginatedList<IntegrityFinding>(
            [.. found.Skip((query.Page - 1) * query.PerPage).Take(query.PerPage)],
            found.Length,
            query.Page,
            query.PerPage));
    }
}
