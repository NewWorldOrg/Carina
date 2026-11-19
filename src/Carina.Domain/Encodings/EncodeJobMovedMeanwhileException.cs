namespace Carina.Domain.Encodings;

public sealed class EncodeJobMovedMeanwhileException(EncodeJobId jobId)
    : InvalidOperationException($"Job {jobId?.Wire} was moved in the ledger by another hand while this one held it, so what this one wrote is dropped.")
{
    public EncodeJobId JobId { get; } = jobId ?? throw new ArgumentNullException(nameof(jobId));
}
