namespace Carina.Domain.Integrity;

/// <summary>
/// What the ledgers claim at one moment: the recording rows, and every file declared beside them by encode work and by
/// the places this process writes into itself.
/// </summary>
public sealed record IntegrityClaims(IReadOnlyList<LedgerFile> Ledger, IReadOnlyList<DeclaredFile> Declared)
{
    /// <summary>
    /// Whether a recording row or a declared file claims the file the finding names, so it is not one nothing owns.
    /// </summary>
    public bool Claim(IntegrityFinding finding) => StrayFileDisposal.Claimed(finding, Ledger, Declared);
}

/// <summary>
/// Reads what the ledgers claim, for a check to compare with the disk and for a file a check found to be checked again
/// just before it is thrown away: the recording rows, the files encode work declares, and the picture, the captions
/// and the record of the data broadcast of every recording, under whichever name the place they lie in is walked.
/// </summary>
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

    /// <summary>
    /// Whether anything claims the file <paramref name="finding"/> names now.
    /// </summary>
    public async Task<bool> ClaimsAsync(IntegrityFinding finding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(finding);

        return (await ReadAsync(cancellationToken)).Claim(finding);
    }
}
