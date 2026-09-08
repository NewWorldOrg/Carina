using Carina.Domain.Migration;

namespace Carina.Infrastructure.Migration;

public sealed class MigrationPassage(
    IMigrationSourceLedger source,
    IMigrationSourceDirectory directory,
    MigrationCarriage carriage,
    IMigrationRecordRepository records,
    IMigrationLease lease,
    TimeProvider clock)
{
    public async Task<MigrationRunId> RunAsync(
        MigrationPass pass,
        IReadOnlySet<ServiceKey> inReach,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inReach);

        IAsyncDisposable held = await lease.TakeAsync(cancellationToken)
            ?? throw new MigrationAlreadyRunningException(
                "A migration is already running, and two of them would take hold of the same files.");

        await using (held)
        {
            DateTime began = clock.GetUtcNow().UtcDateTime;

            SourceLedger ledger = await source.ReadAsync(cancellationToken);
            IReadOnlyList<SourceFile> files = await directory.ListAsync(cancellationToken);

            MigrationRoll settled = await carriage.CarryAsync(
                ledger,
                MigrationClassifier.Over(ledger, files, inReach),
                pass,
                cancellationToken);

            MigrationRunId id = MigrationRunId.New();

            await records.SaveAsync(
                MigrationCensus.Taken(
                    id,
                    ledger.Name,
                    pass,
                    settled,
                    began,
                    clock.GetUtcNow().UtcDateTime),
                cancellationToken);

            return id;
        }
    }
}
