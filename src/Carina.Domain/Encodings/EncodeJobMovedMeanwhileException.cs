namespace Carina.Domain.Encodings;

/// <summary>
/// The ledger's row for a job moved under the hand that was writing it — called off while it ran,
/// as a rule — so what that hand meant to write no longer describes the job. The ledger's word
/// stands; the caller drops what it was doing rather than writing over it.
/// </summary>
public sealed class EncodeJobMovedMeanwhileException(EncodeJobId jobId)
    : InvalidOperationException($"Job {jobId?.Wire} was moved in the ledger by another hand while this one held it, so what this one wrote is dropped.")
{
    public EncodeJobId JobId { get; } = jobId ?? throw new ArgumentNullException(nameof(jobId));
}
