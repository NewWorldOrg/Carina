using Carina.Domain.Encodings;

namespace Carina.TestSupport;

public sealed class HeldEncodeScratch : IEncodeScratchLedger
{
    public List<EncodeScratchFile> Files { get; } = [];

    public List<string> Moves { get; } = [];

    public Task RecordAsync(EncodeScratchFile scratch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scratch);

        Files.Add(scratch);
        Moves.Add($"recorded {scratch.FileName.Value}");

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<EncodeScratchFile>> ListOwedAsync(EncodeJobId jobId, CancellationToken cancellationToken)
    {
        IReadOnlyList<EncodeScratchFile> owed =
        [
            .. Files.Where(scratch => scratch.JobId.Equals(jobId) && scratch.IsOwedARemoval).OrderBy(scratch => scratch.WrittenAt),
        ];

        return Task.FromResult(owed);
    }

    public Task SaveAsync(EncodeScratchFile scratch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scratch);

        Moves.Add($"settled {scratch.FileName.Value} {scratch.Fate}");

        return Task.CompletedTask;
    }
}
