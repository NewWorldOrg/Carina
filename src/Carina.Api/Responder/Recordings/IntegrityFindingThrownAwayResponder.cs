using Carina.Api.Services;

namespace Carina.Api.Responder.Recordings;

public sealed record IntegrityFindingThrownAwayResponder(
    Guid FindingId,
    string OutputRoot,
    string Path,
    long? SizeBytes,
    bool FileRemoved)
{
    public static IntegrityFindingThrownAwayResponder Of(FindingThrownAway thrown)
    {
        ArgumentNullException.ThrowIfNull(thrown);

        return new IntegrityFindingThrownAwayResponder(
            thrown.Finding.Id.Value,
            thrown.Finding.Root.Value,
            thrown.Finding.Path,
            thrown.Finding.ObservedSize,
            thrown.FileRemoved);
    }
}

public sealed record IntegrityFindingRefusedResponder(Guid FindingId, FindingDisposalFailure Refusal)
{
    public static IntegrityFindingRefusedResponder Of(Guid findingId, FindingDisposalFailure refusal)
        => new(findingId, refusal);
}
