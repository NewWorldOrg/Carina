using Carina.Domain.Encodings;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// The one way a job gets a path to write a scratch file to: the ledger is written first, and the
/// path comes back only once it is (BR-ED2-010). A root this process cannot place gives no path.
/// </summary>
public sealed class EncodeScratchFiles(IEncodeScratchLedger ledger, EncodePlaces places, TimeProvider clock)
{
    public async Task<string?> RecordAsync(
        EncodeJob job,
        EncodeScratchKind kind,
        EncodeFileName name,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(name);

        if (places.WhereTheWorkGoes(job.OutputRoot) is not { } room)
        {
            return null;
        }

        await ledger.RecordAsync(
            EncodeScratchFile.Record(
                EncodeScratchFileId.New(),
                job.Id,
                kind,
                job.OutputRoot,
                name,
                clock.GetUtcNow().UtcDateTime),
            cancellationToken);

        return Path.Combine(room, name.Value);
    }
}
