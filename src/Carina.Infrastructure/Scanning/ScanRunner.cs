using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

using Carina.Contracts;
using Carina.Domain.Scans;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Scanning;

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

public sealed class ScanRunner(IServiceScopeFactory scopes, ILogger<ScanRunner> logger) : IDisposable
{
    public const int ProposalsKept = 8;

    public const string UnexpectedEnd = "The scan ended in a way it did not plan for; the log names the failure.";

    private readonly ConcurrentDictionary<ScanRunId, CancellationTokenSource> live = [];
    private readonly ConcurrentDictionary<ScanRunId, ScanProposal> proposals = [];
    private readonly ConcurrentQueue<ScanRunId> order = new();

    public async Task<ScanLaunch> LaunchAsync(ScanScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var announced = new TaskCompletionSource<ScanRunId>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellation = new CancellationTokenSource();
        var walking = Task.Run(
            () => WalkAsync(scope, new Announcement(this, announced, cancellation), cancellation),
            CancellationToken.None);

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

    public bool TryTakeProposal(ScanRunId id, [NotNullWhen(true)] out ScanProposal? proposal)
        => proposals.TryRemove(id, out proposal);

    public void Dispose()
    {
        foreach (var id in live.Keys)
        {
            TryCancel(id);
        }
    }

    private async Task<ScanOutcome> WalkAsync(
        ScanScope scope,
        IScanRunObserver observer,
        CancellationTokenSource cancellation)
    {
        using (cancellation)
        {
            using var scoped = scopes.CreateScope();

            try
            {
                var outcome = await scoped.ServiceProvider
                    .GetRequiredService<IChannelScanOrchestrator>()
                    .RunAsync(scope, observer, cancellation.Token);

                Conclude(outcome);

                return outcome;
            }
            catch (Exception error)
            {
                logger.LogError(error, "A channel scan ended without concluding.");

                return ScanOutcome.CouldNotStart(UnexpectedEnd);
            }
        }
    }

    private void Conclude(ScanOutcome outcome)
    {
        if (outcome.Run is not { } run)
        {
            return;
        }

        live.TryRemove(run.Id, out _);

        if (run.State is not ScanRunState.Completed)
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
        public void Started(ScanRun run)
        {
            runner.live[run.Id] = cancellation;
            announced.TrySetResult(run.Id);
        }
    }
}
