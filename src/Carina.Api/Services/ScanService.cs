using Carina.Api.Common;
using Carina.Domain.Scans;
using Carina.Infrastructure.Scanning;

namespace Carina.Api.Services;

public enum ScanFailure
{
    NoSuchRun = 1,

    StillRunning = 2,

    NeverCompleted = 3,

    ProposalGone = 4,

    ApplyInFlight = 7,

    NotWalkingHere = 5,

    AlreadyEnded = 6,
}

public sealed record ScanProgress(
    ScanRun Run,
    IReadOnlyList<ScanRunAttempt> Attempts,
    ScanDifference? Difference);

public sealed class ScanService(ScanRunner runner, IScanRunRepository runs, ScanApplier applier)
{
    public const int RecentRuns = 20;

    public async Task<ServiceResult<ScanLaunch>> StartAsync(
        ScanScope scope,
        CancellationToken cancellationToken)
        => ServiceResult<ScanLaunch>.Success(await runner.LaunchAsync(scope, cancellationToken));

    public async Task<ServiceResult<ScanProgress, ScanFailure>> ProgressAsync(
        ScanRunId id,
        CancellationToken cancellationToken)
    {
        if (await runs.FindAsync(id, cancellationToken) is not { } run)
        {
            return Missing<ScanProgress>(id);
        }

        return ServiceResult<ScanProgress, ScanFailure>.Success(
            await ProgressOfAsync(run, cancellationToken));
    }

    public async Task<ServiceResult<ScanProgress, ScanFailure>> CancelAsync(
        ScanRunId id,
        CancellationToken cancellationToken)
    {
        if (await runs.FindAsync(id, cancellationToken) is not { } run)
        {
            return Missing<ScanProgress>(id);
        }

        if (!runner.TryCancel(id))
        {
            return ServiceResult<ScanProgress, ScanFailure>.Failure(
                run.IsRunning
                    ? "This scan is not being walked by this process, so there is nothing here to stop."
                    : $"This scan already ended as {run.State}.",
                run.IsRunning ? ScanFailure.NotWalkingHere : ScanFailure.AlreadyEnded);
        }

        return ServiceResult<ScanProgress, ScanFailure>.Success(
            await ProgressOfAsync(run, cancellationToken));
    }

    public async Task<ServiceResult<ScanApplication, ScanFailure>> ApplyAsync(
        ScanRunId id,
        CancellationToken cancellationToken)
    {
        if (await runs.FindAsync(id, cancellationToken) is not { } run)
        {
            return Missing<ScanApplication>(id);
        }

        if (run.IsRunning)
        {
            return ServiceResult<ScanApplication, ScanFailure>.Failure(
                "This scan is still walking; its difference is not settled yet.",
                ScanFailure.StillRunning);
        }

        if (run.State is not ScanRunState.Completed)
        {
            return ServiceResult<ScanApplication, ScanFailure>.Failure(
                $"A scan that ended as {run.State} writes nothing; only a completed one can be applied.",
                ScanFailure.NeverCompleted);
        }

        var claim = runner.TryClaimProposal(id, out var proposal);

        if (claim is ProposalClaim.AlreadyBeingApplied)
        {
            return ServiceResult<ScanApplication, ScanFailure>.Failure(
                "This scan's difference is being applied; ask again once that has finished.",
                ScanFailure.ApplyInFlight);
        }

        if (claim is ProposalClaim.Gone || proposal is null)
        {
            // A claim taken without a proposal behind it would otherwise answer every later
            // apply of this run with "wait", for an apply that is not happening.
            runner.GiveBackProposal(id);

            return ServiceResult<ScanApplication, ScanFailure>.Failure(
                "The difference this scan proposed is no longer held; scan again to propose a fresh one.",
                ScanFailure.ProposalGone);
        }

        try
        {
            var applied = await applier.ApplyAsync(
                proposal.Difference,
                proposal.Systems,
                cancellationToken);

            runner.ProposalApplied(id);

            return ServiceResult<ScanApplication, ScanFailure>.Success(applied);
        }
        catch
        {
            // The write did not land, so the difference is still the difference. Recovering by
            // walking again costs minutes on real hardware.
            runner.GiveBackProposal(id);

            throw;
        }
    }

    public async Task<ServiceResult<IReadOnlyList<ScanRun>>> ListAsync(CancellationToken cancellationToken)
        => ServiceResult<IReadOnlyList<ScanRun>>.Success(
            await runs.ListRecentAsync(RecentRuns, cancellationToken));

    private static ServiceResult<T, ScanFailure> Missing<T>(ScanRunId id)
        => ServiceResult<T, ScanFailure>.Failure(
            $"No scan called '{id.Value}' was ever started here.",
            ScanFailure.NoSuchRun);

    private async Task<ScanProgress> ProgressOfAsync(ScanRun run, CancellationToken cancellationToken)
        => new(
            run,
            await runs.ListAttemptsAsync(run.Id, cancellationToken),
            runner.TryPeekProposal(run.Id, out var proposal) ? proposal.Difference : null);
}
