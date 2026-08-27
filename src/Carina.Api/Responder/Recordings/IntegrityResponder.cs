using Carina.Domain.Base;
using Carina.Domain.Integrity;

namespace Carina.Api.Responder.Recordings;

public sealed record IntegrityCheckResponder(
    Guid Id,
    DateTime StartedAt,
    DateTime FinishedAt,
    int RootsWalked,
    int RootsOutOfReach,
    int FilesRead,
    int LedgerRowsRead,
    int LedgerRowsJudged,
    int LedgerRowsStillWriting,
    int LedgerRowsInRootsOutOfReach)
{
    public static IntegrityCheckResponder Of(IntegrityCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);

        return new IntegrityCheckResponder(
            check.Id.Value,
            check.StartedAt,
            check.FinishedAt,
            check.RootsWalked,
            check.RootsOutOfReach,
            check.FilesRead,
            check.LedgerRowsRead,
            check.LedgerRowsJudged,
            check.LedgerRowsStillWriting,
            check.LedgerRowsInRootsOutOfReach);
    }
}

public sealed record IntegrityFindingResponder(
    Guid Id,
    IntegrityFault Fault,
    string OutputRoot,
    string Path,
    string? RecordingId,
    long? LedgerSize,
    long? ObservedSize,
    DateTime NoticedAt)
{
    public static IntegrityFindingResponder Of(IntegrityFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        return new IntegrityFindingResponder(
            finding.Id.Value,
            finding.Fault,
            finding.Root.Value,
            finding.Path,
            finding.RecordingId?.Wire,
            finding.LedgerSize,
            finding.ObservedSize,
            finding.NoticedAt);
    }
}

public sealed record IntegrityListResponder(
    IntegrityCheckResponder? Check,
    IReadOnlyList<IntegrityFindingResponder> Items,
    int Total,
    int CurrentPage,
    int LastPage,
    int PerPage)
{
    public static IntegrityListResponder Of(IntegrityCheck? check, PaginatedList<IntegrityFinding> found)
    {
        ArgumentNullException.ThrowIfNull(found);

        return new IntegrityListResponder(
            check is null ? null : IntegrityCheckResponder.Of(check),
            [.. found.Items.Select(IntegrityFindingResponder.Of)],
            found.Total,
            found.CurrentPage,
            found.LastPage,
            found.PerPage);
    }
}

public sealed record IntegritySweepResponder(IntegrityCheckResponder Check, int Findings)
{
    public static IntegritySweepResponder Of(IntegrityCheck check, int findings)
        => new(IntegrityCheckResponder.Of(check), findings);
}

public sealed record IntegritySweepRefusedResponder(
    SweepRefusal Refusal,
    Guid? RunningCheckId,
    DateTimeOffset? NotBefore)
{
    public static IntegritySweepRefusedResponder Of(SweepVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        return new IntegritySweepRefusedResponder(
            verdict.Refusal,
            verdict.RunningId?.Value,
            verdict.NotBefore is null ? null : new DateTimeOffset(verdict.NotBefore.Value, TimeSpan.Zero));
    }
}
