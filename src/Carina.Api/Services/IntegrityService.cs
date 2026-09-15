using Carina.Api.Common;
using Carina.Domain.Base;
using Carina.Domain.Integrity;
using Carina.Infrastructure.Integrity;

namespace Carina.Api.Services;

public sealed record IntegrityFindings(IntegrityCheck? Check, PaginatedList<IntegrityFinding> Findings);

public sealed record IntegritySweep(IntegrityCheck? Swept, int Findings, SweepVerdict Verdict);

public sealed record FindingThrownAway(IntegrityFinding Finding, bool FileRemoved);

public sealed class IntegrityService(
    IntegrityCheckJob sweeps,
    IIntegrityCheckRepository checks,
    IRecordingLedger ledger,
    IEncodeWorkLedger encodeWork,
    IStrayFileEraser strays,
    FindingDisposals disposals,
    IntegritySettings settings,
    TimeProvider clock,
    ILogger<IntegrityService> logger)
{
    public async Task<ServiceResult<IntegrityFindings>> ListAsync(
        IntegrityFindingQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IntegrityCheck? latest = await checks.LatestAsync(cancellationToken);

        return ServiceResult<IntegrityFindings>.Success(new IntegrityFindings(
            latest,
            latest is null
                ? new PaginatedList<IntegrityFinding>([], 0, query.Page, query.PerPage)
                : await checks.ListFindingsAsync(latest.Id, query, cancellationToken)));
    }

    public async Task<ServiceResult<IntegritySweep>> RunAsync(CancellationToken cancellationToken)
    {
        IntegrityCheck? latest = await checks.LatestAsync(cancellationToken);
        SweepVerdict asked = SweepGuard.Of(
            sweeps.RunningCheck,
            latest?.FinishedAt,
            clock.GetUtcNow().UtcDateTime,
            settings.BetweenManualSweeps);

        if (!asked.IsAllowed)
        {
            return ServiceResult<IntegritySweep>.Success(new IntegritySweep(null, 0, asked));
        }

        IntegrityRun run = await sweeps.RunAsync(cancellationToken);

        return run.Swept is { } swept
            ? ServiceResult<IntegritySweep>.Success(
                new IntegritySweep(swept.Check, swept.Findings.Count, SweepVerdict.Allowed))
            : ServiceResult<IntegritySweep>.Success(new IntegritySweep(
                null,
                0,
                new SweepVerdict(SweepRefusal.OneIsAlreadyRunning, run.Running, null)));
    }

    public async Task<ServiceResult<FindingThrownAway, FindingDisposalFailure>> ThrowAwayAsync(
        IntegrityFindingId id,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        IntegrityCheck? latest = await checks.LatestAsync(cancellationToken);

        if (latest is null || await checks.FindFindingAsync(latest.Id, id, cancellationToken) is not { } finding)
        {
            return Refused(
                $"The most recent check found nothing named {id.Value}, and only what the most recent check "
                + "found can be thrown away.",
                FindingDisposalFailure.NoSuchFinding);
        }

        if (StrayFileDisposal.Refusal(finding) is { } refusal)
        {
            return Refused(Because(refusal, finding), Failed(refusal));
        }

        using IDisposable? turn = disposals.Begin(id);

        if (turn is null)
        {
            return Refused(
                $"The file finding {disposals.Underway?.Value} names is being thrown away; only one is at a "
                + "time, so this one waits rather than running beside it.",
                FindingDisposalFailure.OneIsAlreadyBeingThrownAway);
        }

        IReadOnlyList<LedgerFile> rows = await ledger.ListAsync(cancellationToken);
        IReadOnlyList<DeclaredFile> declared = await encodeWork.ListAsync(cancellationToken);

        if (StrayFileDisposal.Claimed(finding, rows, declared))
        {
            return Refused(
                $"'{finding.Path}' under output root '{finding.Root.Value}' is claimed now by a recording or by "
                + "encode work still in hand, so it is no longer a file nothing owns and it is left where it is.",
                FindingDisposalFailure.FileChanged);
        }

        using var limit = new CancellationTokenSource(disposals.Longest, clock);
        using CancellationTokenSource asking =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, limit.Token);

        StrayFileErasure erasure;

        try
        {
            erasure = await strays.EraseAsync(finding, asking.Token);
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested
                                                 && !cancellationToken.IsCancellationRequested)
        {
            return Refused(
                $"Throwing '{finding.Path}' away was still going after {disposals.Longest}, so it was given up "
                + "on. The finding is still listed, and asking again checks the file again first.",
                FindingDisposalFailure.TookTooLong);
        }

        if (erasure.Fault is { } fault)
        {
            return Refused(erasure.Note!, Failed(fault));
        }

        bool thrownAway = await checks.ThrowAwayFindingAsync(
            latest.Id,
            id,
            clock.GetUtcNow().UtcDateTime,
            CancellationToken.None);

        if (!thrownAway)
        {
            logger.LogWarning(
                "The file '{Path}' under output root '{Root}' was erased, but the finding {FindingId} that "
                + "named it was not marked thrown away.",
                finding.Path,
                finding.Root.Value,
                id.Value);
        }

        return ServiceResult<FindingThrownAway, FindingDisposalFailure>.Success(
            new FindingThrownAway(finding, erasure.FileRemoved));
    }

    private static string Because(StrayFileRefusal refusal, IntegrityFinding finding) => refusal switch
    {
        StrayFileRefusal.NamesARecording =>
            $"This finding is about recording {finding.RecordingId?.Wire}, whose row owns the file; a recording "
            + "is thrown away whole through DELETE /api/recordings/{id}, never one file at a time from here.",
        StrayFileRefusal.NothingOnTheDisk =>
            $"This finding is recording {finding.RecordingId?.Wire}, whose file is missing, so there is no file "
            + "to throw away; a recording row goes through DELETE /api/recordings/{id}.",
        StrayFileRefusal.AlreadyThrownAway => "The file this finding names has already been thrown away.",
        StrayFileRefusal.NoTimeWasTaken =>
            "The check that found this file did not keep when it was last written, so nothing can tell whether "
            + "it changed since; run the check again and throw away what that check finds.",
        _ => throw new ArgumentOutOfRangeException(
            nameof(refusal),
            refusal,
            "A finding is refused in one of the ways this type holds."),
    };

    private static FindingDisposalFailure Failed(StrayFileRefusal refusal) => refusal switch
    {
        StrayFileRefusal.NamesARecording => FindingDisposalFailure.NamesARecording,
        StrayFileRefusal.NothingOnTheDisk => FindingDisposalFailure.NothingOnTheDisk,
        StrayFileRefusal.AlreadyThrownAway => FindingDisposalFailure.AlreadyThrownAway,
        StrayFileRefusal.NoTimeWasTaken => FindingDisposalFailure.NoTimeWasTaken,
        _ => throw new ArgumentOutOfRangeException(
            nameof(refusal),
            refusal,
            "A finding is refused in one of the ways this type holds."),
    };

    private static FindingDisposalFailure Failed(StrayErasureFault fault) => fault switch
    {
        StrayErasureFault.RootOutOfReach => FindingDisposalFailure.RootOutOfReach,
        StrayErasureFault.FileChanged => FindingDisposalFailure.FileChanged,
        StrayErasureFault.BeingWritten => FindingDisposalFailure.StillBeingWritten,
        StrayErasureFault.FileLeftBehind => FindingDisposalFailure.FilesLeftBehind,
        StrayErasureFault.DriverUnreachable => FindingDisposalFailure.DriverUnreachable,
        StrayErasureFault.DriverRefused => FindingDisposalFailure.DriverRefused,
        _ => throw new ArgumentOutOfRangeException(
            nameof(fault),
            fault,
            "An erasure that failed says which way it failed."),
    };

    private static ServiceResult<FindingThrownAway, FindingDisposalFailure> Refused(
        string message,
        FindingDisposalFailure failure)
        => ServiceResult<FindingThrownAway, FindingDisposalFailure>.Failure(message, failure);
}
