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

    public HeldSurvey Declaring(OutputRoot root, params (string Name, long SizeBytes)[] files)
    {
        roots.Add(root);
        listings[root.Value] = RootListing.Of(
            root,
            [.. files.Select(file => new StoredFile(file.Name, file.SizeBytes))]);

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

internal sealed class HeldReports : IIntegrityReportStore
{
    public List<IntegritySweep> Saved { get; } = [];

    public Task SaveAsync(IntegritySweep sweep, CancellationToken cancellationToken)
    {
        Saved.Add(sweep);

        return Task.CompletedTask;
    }

    public Task<IntegritySweep?> LatestAsync(CancellationToken cancellationToken)
        => Task.FromResult(Saved.Count is 0 ? null : Saved[^1]);
}
