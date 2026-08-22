using Carina.Domain.Channels;
using Carina.Domain.Scans;

namespace Carina.TestSupport;

public sealed class ScriptedScanOrchestrator(HeldScanRuns runs) : IChannelScanOrchestrator
{
    public static readonly DateTime At = new(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);

    private readonly SemaphoreSlim released = new(0);

    public string InstanceId { get; set; } = "instance-a";

    public string? CouldNotStart { get; set; }

    public bool HoldsOpen { get; set; }

    public string? ThrowsAfterAnnouncing { get; set; }

    public bool EveryAttemptFails { get; set; }

    public ScanDifference Difference { get; set; } = ScanDifference.Nothing;

    public List<TuningParameters> Walked { get; } = [];

    public List<ScanAttemptOutcome> Outcomes { get; } = [];

    public List<ScanScope> Scopes { get; } = [];

    public TaskCompletionSource Announced { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<ScanOutcome> RunAsync(ScanScope scope, CancellationToken cancellationToken)
        => RunAsync(scope, UnwatchedScanRun.Instance, cancellationToken);

    public async Task<ScanOutcome> RunAsync(
        ScanScope scope,
        IScanRunObserver observer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observer);

        Scopes.Add(scope);

        if (CouldNotStart is { } refusal)
        {
            return ScanOutcome.CouldNotStart(refusal);
        }

        ScanRunStart start = await runs.StartAsync(
            ScanRun.Start(ScanRunId.New(), InstanceId, At),
            cancellationToken);

        if (start.Started is not { } run)
        {
            return ScanOutcome.RefusedBecauseOneIsRunning(start.AlreadyRunning);
        }

        observer.Started(run);
        Announced.TrySetResult();

        if (ThrowsAfterAnnouncing is { } failure)
        {
            throw new InvalidOperationException(failure);
        }

        var attempts = new List<ScanRunAttempt>();

        for (int step = 0; step < Walked.Count; step++)
        {
            ScanAttemptOutcome outcome = Outcomes.Count > step
                ? Outcomes[step]
                : EveryAttemptFails
                    ? ScanAttemptOutcome.NoLock
                    : ScanAttemptOutcome.Succeeded;
            bool locked = outcome is not ScanAttemptOutcome.NoLock;

            var attempt = ScanRunAttempt.Rehydrate(
                ScanRunAttemptId.New(),
                run.Id,
                Walked[step],
                outcome,
                locked ? SignalMeasurement.WithLock(At, 21_500) : SignalMeasurement.WithoutLock(At),
                null,
                outcome is ScanAttemptOutcome.Succeeded ? null : $"scripted {outcome}",
                At,
                At);

            attempts.Add(attempt);
            await runs.AddAttemptAsync(attempt, CancellationToken.None);
        }

        if (HoldsOpen)
        {
            try
            {
                await released.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }

        bool cancelled = cancellationToken.IsCancellationRequested;

        if (cancelled)
        {
            ScanConclusion.Stop(run, observer.Stop, At);
        }
        else
        {
            run.Complete(At);
        }

        await runs.SaveAsync(run, CancellationToken.None);

        return ScanOutcome.Of(run, attempts, cancelled ? ScanDifference.Nothing : Difference);
    }

    public void Release() => released.Release();
}
