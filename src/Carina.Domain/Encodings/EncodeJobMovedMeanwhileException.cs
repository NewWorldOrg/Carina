namespace Carina.Domain.Encodings;

/// <summary>
/// Thrown when the ledger's row for a job changed under the hand that was writing it, such as a job
/// called off while it ran. The caller drops what it was doing rather than writing over it.
/// </summary>
public sealed class EncodeJobMovedMeanwhileException(EncodeJobId jobId)
    : InvalidOperationException($"Job {jobId?.Wire} was moved in the ledger by another hand while this one held it, so what this one wrote is dropped.")
{
    public EncodeJobId JobId { get; } = jobId ?? throw new ArgumentNullException(nameof(jobId));
}
