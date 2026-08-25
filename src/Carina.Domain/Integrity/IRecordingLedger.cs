namespace Carina.Domain.Integrity;

public interface IRecordingLedger
{
    Task<IReadOnlyList<LedgerFile>> ListAsync(CancellationToken cancellationToken);
}
