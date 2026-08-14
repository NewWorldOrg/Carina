using Carina.Domain.Channels;
using Carina.Domain.Scans;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ScanRunRepositoryTests(RepositoryDatabase database)
{
    private static readonly DateTime At = new(2026, 8, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task TheSecondStartIsRefusedAndNamesTheScanAlreadyRunning()
    {
        await ClearAsync();
        await using var context = database.Open();
        var runs = new ScanRunRepository(context);
        var first = await runs.StartAsync(ScanRun.Start(ScanRunId.New(), "instance-a", At), Cancel);

        await using var other = database.Open();
        var second = await new ScanRunRepository(other)
            .StartAsync(ScanRun.Start(ScanRunId.New(), "instance-a", At.AddMinutes(1)), Cancel);

        Assert.True(first.WasStarted);
        Assert.False(second.WasStarted);
        Assert.Equal(first.Started!.Id, second.AlreadyRunning);
    }

    [Fact]
    public async Task AScanThatEndedLetsTheNextOneStart()
    {
        await ClearAsync();
        await using var context = database.Open();
        var runs = new ScanRunRepository(context);
        var first = await runs.StartAsync(ScanRun.Start(ScanRunId.New(), "instance-a", At), Cancel);

        first.Started!.Complete(At.AddMinutes(3));
        await runs.SaveAsync(first.Started, Cancel);

        await using var other = database.Open();
        var second = await new ScanRunRepository(other)
            .StartAsync(ScanRun.Start(ScanRunId.New(), "instance-a", At.AddMinutes(4)), Cancel);

        Assert.True(second.WasStarted);
        Assert.Null(second.AlreadyRunning);
    }

    [Fact]
    public async Task ADriverThatCameBackAsAnotherInstanceEndsTheScanAsInterrupted()
    {
        await ClearAsync();
        await using var context = database.Open();
        var runs = new ScanRunRepository(context);
        var started = await runs.StartAsync(ScanRun.Start(ScanRunId.New(), "instance-a", At), Cancel);

        started.Started!.Interrupt(At.AddMinutes(2));
        await runs.SaveAsync(started.Started, Cancel);

        await using var reading = database.Open();
        var stored = await new ScanRunRepository(reading).FindAsync(started.Started.Id, Cancel);

        Assert.Equal(ScanRunState.Interrupted, stored!.State);
        Assert.Equal("instance-a", stored.DriverInstanceId);
        Assert.Null(await new ScanRunRepository(reading).FindRunningAsync(Cancel));
    }

    [Fact]
    public async Task AFailureKeepsItsReasonThroughTheRoundTrip()
    {
        await ClearAsync();
        await using var context = database.Open();
        var runs = new ScanRunRepository(context);
        var started = await runs.StartAsync(ScanRun.Start(ScanRunId.New(), "instance-a", At), Cancel);

        started.Started!.Fail("every tuner was busy for longer than the bounded wait", At.AddMinutes(1));
        await runs.SaveAsync(started.Started, Cancel);

        await using var reading = database.Open();
        var stored = await new ScanRunRepository(reading).FindAsync(started.Started.Id, Cancel);

        Assert.Equal(ScanRunState.Failed, stored!.State);
        Assert.Equal("every tuner was busy for longer than the bounded wait", stored.Reason);
    }

    [Fact]
    public async Task EveryAttemptKeepsTheTuningAndTheWayItFailed()
    {
        await ClearAsync();
        await using var context = database.Open();
        var runs = new ScanRunRepository(context);
        var started = await runs.StartAsync(ScanRun.Start(ScanRunId.New(), "instance-a", At), Cancel);
        var run = started.Started!;

        await runs.AddAttemptAsync(
            Attempt(run.Id, TuningParameters.Terrestrial(27), ScanAttemptOutcome.NoLock, null, null, 0),
            Cancel);
        await runs.AddAttemptAsync(
            Attempt(
                run.Id,
                TuningParameters.Bs(15, new TransportStreamId(0x40F0)),
                ScanAttemptOutcome.UnexpectedStream,
                SignalMeasurement.WithLock(At, 19_000),
                new TransportStreamId(0x40F1),
                30),
            Cancel);

        await using var reading = database.Open();
        var attempts = await new ScanRunRepository(reading).ListAttemptsAsync(run.Id, Cancel);

        Assert.Equal(2, attempts.Count);
        Assert.Equal(ScanAttemptOutcome.NoLock, attempts[0].Outcome);
        Assert.Equal(TuneSystem.IsdbT, attempts[0].Tuning.System);
        Assert.Null(attempts[0].Measurement);
        Assert.Equal(new TransportStreamId(0x40F0), attempts[1].Tuning.TransportStreamId);
        Assert.Equal(new TransportStreamId(0x40F1), attempts[1].ObservedTransportStreamId);
        Assert.Equal(19_000, attempts[1].Measurement?.CnrMilliDecibels);
    }

    [Fact]
    public async Task AStartThatIsNotRunningIsRefusedBeforeItReachesTheDatabase()
    {
        await ClearAsync();
        await using var context = database.Open();
        var run = ScanRun.Start(ScanRunId.New(), "instance-a", At);
        run.Complete(At.AddMinutes(1));

        await Assert.ThrowsAsync<ArgumentException>(
            () => new ScanRunRepository(context).StartAsync(run, Cancel));
    }

    private static ScanRunAttempt Attempt(
        ScanRunId scanRunId,
        TuningParameters tuning,
        ScanAttemptOutcome outcome,
        SignalMeasurement? measurement,
        TransportStreamId? observed,
        int secondsIn)
        => ScanRunAttempt.Rehydrate(
            ScanRunAttemptId.New(),
            scanRunId,
            tuning,
            outcome,
            measurement,
            observed,
            null,
            At.AddSeconds(secondsIn),
            At.AddSeconds(secondsIn + 9));

    private async Task ClearAsync()
    {
        await using var context = database.Open();
        await context.Database.ExecuteSqlRawAsync("DELETE FROM scan_run", Cancel);
    }
}
