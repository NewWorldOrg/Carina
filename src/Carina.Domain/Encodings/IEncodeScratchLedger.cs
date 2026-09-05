namespace Carina.Domain.Encodings;

public interface IEncodeScratchLedger
{
    Task RecordAsync(EncodeScratchFile scratch, CancellationToken cancellationToken);

    Task<IReadOnlyList<EncodeScratchFile>> ListOwedAsync(EncodeJobId jobId, CancellationToken cancellationToken);

    Task SaveAsync(EncodeScratchFile scratch, CancellationToken cancellationToken);
}
