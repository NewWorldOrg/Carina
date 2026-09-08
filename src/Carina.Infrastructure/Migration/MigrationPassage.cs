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
        IReadOnlyList<RescannedService> rescanned,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rescanned);

        IAsyncDisposable held = await lease.TakeAsync(cancellationToken)
            ?? throw new MigrationAlreadyRunningException(
                "A migration is already running, and two of them would take hold of the same files.");

        await using (held)
        {
            DateTime began = clock.GetUtcNow().UtcDateTime;

            SourceLedger ledger = await source.ReadAsync(cancellationToken);
            IReadOnlyList<SourceFile> files = await directory.ListAsync(cancellationToken);
            MigrationRunId id = MigrationRunId.New();

            MigrationCarried settled = await carriage.CarryAsync(
                id,
                ledger,
                rescanned,
                MigrationClassifier.Over(ledger, files, RescannedService.InReach(rescanned)),
                pass,
                cancellationToken);

            await records.SaveAsync(
                MigrationCensus.Taken(
                    id,
                    ledger.Name,
                    pass,
                    settled.Roll,
                    settled.Aftermath,
                    began,
                    clock.GetUtcNow().UtcDateTime),
                cancellationToken);

            return id;
        }
    }
}
