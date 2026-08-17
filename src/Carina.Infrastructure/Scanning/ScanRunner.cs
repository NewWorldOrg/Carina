using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

using Carina.Contracts;
using Carina.Domain.Scans;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Scanning;

public enum ProposalClaim
{
    Claimed = 1,

    /// <summary>Another apply holds it. Waiting is the recovery, not walking again.</summary>
    AlreadyBeingApplied = 2,

    Gone = 3,
}

public sealed record ScanProposal(
    ScanRunId ScanRunId,
    ScanDifference Difference,
    IReadOnlyList<TuneSystem> Systems);

public sealed record ScanLaunch
{
    private ScanLaunch(ScanRunId? started, ScanRunId? alreadyRunning, string? couldNotStart)
    {
        Started = started;
        AlreadyRunning = alreadyRunning;
        CouldNotStartBecause = couldNotStart;
    }

    public ScanRunId? Started { get; }

    public ScanRunId? AlreadyRunning { get; }

    public string? CouldNotStartBecause { get; }

    public bool WasStarted => Started is not null;

    public static ScanLaunch Of(ScanRunId started)
    {
        ArgumentNullException.ThrowIfNull(started);

        return new ScanLaunch(started, null, null);
    }

    public static ScanLaunch RefusedBecauseOneIsRunning(ScanRunId? alreadyRunning)
        => new(null, alreadyRunning, null);

    public static ScanLaunch CouldNotStart(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new ScanLaunch(null, null, reason);
    }
}

public sealed class ScanRunner(IServiceScopeFactory scopes, ILogger<ScanRunner> logger) : IHostedService
{
    public const int ProposalsKept = 8;

    public const string UnexpectedEnd = "the scan ended without concluding; the app log names the failure";

    private readonly ConcurrentDictionary<ScanRunId, CancellationTokenSource> live = [];
    private readonly ConcurrentDictionary<ScanRunId, ScanProposal> proposals = [];
    private readonly ConcurrentDictionary<ScanRunId, byte> claimed = [];
    private readonly ConcurrentDictionary<Task, byte> walks = [];
    private readonly ConcurrentQueue<ScanRunId> order = new();

    private volatile bool stopping;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await AbandonWhatAnEarlierProcessLeftAsync(cancellationToken);
        }
        catch (Exception error)
        {
            logger.LogError(
                error,
                "A scan left behind by an earlier process could not be settled at startup.");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        stopping = true;

        foreach (var id in live.Keys)
        {
            TryCancel(id);
        }

        var pending = walks.Keys.ToArray();

        if (pending.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(pending).WaitAsync(cancellationToken);
        }
        catch (Exception error) when (error is OperationCanceledException or TimeoutException)
        {
            logger.LogWarning(
                "The app stopped before {Count} scan walk(s) had finished settling.",
                pending.Length);
        }
    }

    public async Task<ScanLaunch> LaunchAsync(ScanScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var announced = new TaskCompletionSource<ScanRunId>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellation = new CancellationTokenSource();
        var announcement = new Announcement(this, announced, cancellation);
        var walking = Task.Run(
            () => WalkAsync(scope, announcement, cancellation),
            CancellationToken.None);

        walks[walking] = 0;
        _ = walking.ContinueWith(
            finished => walks.TryRemove(finished, out _),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        await Task.WhenAny(announced.Task, walking).WaitAsync(cancellationToken);

        if (announced.Task.IsCompletedSuccessfully)
        {
            return ScanLaunch.Of(announced.Task.Result);
        }

        var outcome = await walking;

        return outcome.CouldNotStartBecause is { } reason
            ? ScanLaunch.CouldNotStart(reason)
            : ScanLaunch.RefusedBecauseOneIsRunning(outcome.AlreadyRunning);
    }

    public bool IsWalking(ScanRunId id) => live.ContainsKey(id);

    public bool TryCancel(ScanRunId id)
    {
        if (!live.TryGetValue(id, out var cancellation))
        {
            return false;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        return true;
    }

    public bool TryPeekProposal(ScanRunId id, [NotNullWhen(true)] out ScanProposal? proposal)
        => proposals.TryGetValue(id, out proposal);

    /// <summary>
    /// Claims a proposal for one apply. A second claim is refused while the first is out, which
    /// is what keeps two applies of one scan from both writing it. The proposal itself stays
    /// remembered until the write lands, so a walk that cost minutes is not spent by an apply
    /// that never committed — and a caller told it cannot have the proposal is told which of
    /// the two reasons applies, because only one of them means walking again.
    /// </summary>
    public ProposalClaim TryClaimProposal(ScanRunId id, out ScanProposal? proposal)
    {
        proposal = null;

        // Asked before claiming, so that a run with nothing to apply is told to walk again
        // rather than to wait for an apply that is not happening.
        if (!proposals.ContainsKey(id))
        {
            return ProposalClaim.Gone;
        }

        if (!claimed.TryAdd(id, 0))
        {
            return ProposalClaim.AlreadyBeingApplied;
        }

        if (proposals.TryGetValue(id, out proposal))
        {
            return ProposalClaim.Claimed;
        }

        claimed.TryRemove(id, out _);

        return ProposalClaim.Gone;
    }

    /// <summary>Forgets a proposal whose apply committed.</summary>
    public void ProposalApplied(ScanRunId id)
    {
        ArgumentNullException.ThrowIfNull(id);

        proposals.TryRemove(id, out _);
        claimed.TryRemove(id, out _);
    }

    /// <summary>
    /// Releases a claim whose apply did not commit. The proposal was never removed, so nothing
    /// has to be put back and the eviction order is left as it was.
    /// </summary>
    public void GiveBackProposal(ScanRunId id)
    {
        ArgumentNullException.ThrowIfNull(id);

        claimed.TryRemove(id, out _);
    }

    private async Task AbandonWhatAnEarlierProcessLeftAsync(CancellationToken cancellationToken)
    {
        using var scoped = scopes.CreateScope();

        var runs = scoped.ServiceProvider.GetRequiredService<IScanRunRepository>();

        if (await runs.FindRunningAsync(cancellationToken) is not { } abandoned)
        {
            return;
        }

        ScanConclusion.Abandon(
            abandoned,
            scoped.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime);

        await runs.SaveAsync(abandoned, cancellationToken);

        logger.LogWarning(
            "A scan left running by an earlier process was settled so that scanning is possible again.");
    }

    private async Task<ScanOutcome> WalkAsync(
        ScanScope scope,
        Announcement announcement,
        CancellationTokenSource cancellation)
    {
        try
        {
            using var scoped = scopes.CreateScope();

            try
            {
                var outcome = await scoped.ServiceProvider
                    .GetRequiredService<IChannelScanOrchestrator>()
                    .RunAsync(scope, announcement, cancellation.Token);

                Remember(outcome);

                return outcome;
            }
            catch (Exception error)
            {
                logger.LogError(error, "A channel scan ended without concluding.");

                await SettleAsync(announcement.Run);

                return ScanOutcome.CouldNotStart(UnexpectedEnd);
            }
        }
        finally
        {
            if (announcement.Run is { } run)
            {
                live.TryRemove(run.Id, out _);
            }

            cancellation.Dispose();
        }
    }

    private async Task SettleAsync(ScanRun? started)
    {
        if (started is null)
        {
            return;
        }

        try
        {
            using var scoped = scopes.CreateScope();

            var runs = scoped.ServiceProvider.GetRequiredService<IScanRunRepository>();

            if (await runs.FindAsync(started.Id, CancellationToken.None) is not { IsRunning: true } stuck)
            {
                return;
            }

            stuck.Fail(
                UnexpectedEnd,
                scoped.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime);

            await runs.SaveAsync(stuck, CancellationToken.None);
        }
        catch (Exception error)
        {
            logger.LogError(
                error,
                "A scan that ended without concluding could not be settled; scanning stays blocked until it is.");
        }
    }

    private void Remember(ScanOutcome outcome)
    {
        if (outcome.Run is not { State: ScanRunState.Completed } run)
        {
            return;
        }

        proposals[run.Id] = new ScanProposal(
            run.Id,
            outcome.Difference,
            [.. outcome.Attempts.Select(attempt => attempt.Tuning.System).Distinct()]);
        order.Enqueue(run.Id);

        while (order.Count > ProposalsKept && order.TryDequeue(out var oldest))
        {
            proposals.TryRemove(oldest, out _);
        }
    }

    private sealed class Announcement(
        ScanRunner runner,
        TaskCompletionSource<ScanRunId> announced,
        CancellationTokenSource cancellation) : IScanRunObserver
    {
        public ScanRun? Run { get; private set; }

        public ScanStop Stop => runner.stopping
            ? ScanStop.BecauseTheAppIsStopping
            : ScanStop.AsRequested;

        public void Started(ScanRun run)
        {
            Run = run;
            runner.live[run.Id] = cancellation;
            announced.TrySetResult(run.Id);
        }
    }
}
