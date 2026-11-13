using Carina.Domain.Base;
using Carina.Domain.Migration;

using static Carina.TestSupport.CarriedMigrationFixtures;

namespace Carina.Infrastructure.Tests.Fixtures.Migration;

internal sealed class CarryLog
{
    public int Id { get; set; }

    public int TapeEntryId { get; set; }
}

internal sealed class HeldMigrationRecords : IMigrationRecordRepository
{
    public List<MigrationReport> Saved { get; } = [];

    public Task SaveAsync(MigrationReport report, CancellationToken cancellationToken)
    {
        Saved.Add(report);

        return Task.CompletedTask;
    }

    public Task<MigrationRun?> LatestAsync(CancellationToken cancellationToken)
        => Task.FromResult(Saved.LastOrDefault()?.Run);

    public Task<MigrationReport?> ReadAsync(MigrationRunId runId, CancellationToken cancellationToken)
        => Task.FromResult(Saved.FirstOrDefault(report => report.Run.Id.Equals(runId)));

    public Task<MigrationRecordSummary?> SummariseAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<PaginatedList<MigrationDetail>> ListDetailsAsync(
        MigrationRunId runId,
        MigrationDetailQuery query,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();
}

internal sealed class OneAtATime : IMigrationLease
{
    public bool Held { get; set; }

    public int Taken { get; private set; }

    public int Returned { get; private set; }

    public Task<IAsyncDisposable?> TakeAsync(CancellationToken cancellationToken)
    {
        if (Held)
        {
            return Task.FromResult<IAsyncDisposable?>(null);
        }

        Held = true;
        Taken++;

        return Task.FromResult<IAsyncDisposable?>(new Hold(this));
    }

    private sealed class Hold(OneAtATime lease) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            lease.Held = false;
            lease.Returned++;

            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class SourceOnDisk(string directory) : IMigrationSourceLedger, IMigrationSourceDirectory
{
    public Task<SourceLedger> ReadAsync(CancellationToken cancellationToken)
        => Task.FromResult(Ledger(
            [Recording(7), Recording(8)],
            [AsBroadcast(7, "one.m2ts", 11), AsBroadcast(8, "empty.m2ts", 4096)]));

    public Task<IReadOnlyList<SourceFile>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SourceFile>>(
            [.. Directory
                .GetFiles(directory)
                .Order(StringComparer.Ordinal)
                .Select(file => OnDisk(Path.GetFileName(file), new FileInfo(file).Length))]);
}
