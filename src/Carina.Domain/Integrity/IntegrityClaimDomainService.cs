namespace Carina.Domain.Integrity;

public sealed record IntegrityClaims(IReadOnlyList<LedgerFile> Ledger, IReadOnlyList<DeclaredFile> Declared)
{
    public bool Claim(IntegrityFinding finding) => StrayFileDisposal.Claimed(finding, Ledger, Declared);
}

public sealed class IntegrityClaimDomainService(
    IRecordingLedger ledger,
    IEncodeWorkLedger encodeWork,
    IWrittenFileSurvey written)
{
    public async Task<IntegrityClaims> ReadAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<LedgerFile> rows = await ledger.ListAsync(cancellationToken);

        return new IntegrityClaims(rows, written.Claimed(rows, await encodeWork.ListAsync(cancellationToken)));
    }

    public async Task<bool> ClaimsAsync(IntegrityFinding finding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(finding);

        return (await ReadAsync(cancellationToken)).Claim(finding);
    }
}
