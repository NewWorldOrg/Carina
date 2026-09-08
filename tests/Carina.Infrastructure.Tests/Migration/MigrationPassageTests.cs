using Carina.Domain.Migration;
using Carina.Infrastructure.Migration;
using Carina.TestSupport;

using static Carina.Infrastructure.Tests.Migration.CarriedMigrationFixtures;

namespace Carina.Infrastructure.Tests.Migration;

public sealed class MigrationPassageTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly MigrationJournal journal = new();

    private readonly HandTurnedClock clock = new(new DateTimeOffset(2026, 9, 8, 5, 0, 0, TimeSpan.Zero));

    private readonly ScriptedCarrier carrier;

    private readonly HeldMigratedRecordings recordings;

    private readonly HeldMigrationRecords records = new();

    private readonly OneAtATime lease = new();

    private readonly ReadOnlySource source = new();

    public MigrationPassageTests()
    {
        carrier = new ScriptedCarrier(journal);
        recordings = new HeldMigratedRecordings(journal);
    }

    [Fact]
    public async Task ASecondStartWhileOneIsRunningIsRefused()
    {
        lease.Held = true;

        MigrationAlreadyRunningException refused = await Assert.ThrowsAsync<MigrationAlreadyRunningException>(
            () => Passage().RunAsync(MigrationPass.ForReal, Rescanned(), Cancel));

        Assert.Contains("already running", refused.Message, StringComparison.Ordinal);
        Assert.Equal(0, source.Reads);
        Assert.Empty(journal.Steps);
        Assert.Empty(records.Saved);
    }

    [Fact]
    public async Task TheLeaseIsGivenBackWhenTheRunIsOver()
    {
        await Passage().RunAsync(MigrationPass.ForReal, Rescanned(), Cancel);

        Assert.Equal(1, lease.Taken);
        Assert.Equal(1, lease.Returned);
    }

    [Fact]
    public async Task TheLeaseIsGivenBackWhenTheRunStops()
    {
        carrier.Answers(
            "one.m2ts",
            new MigrationCarry(
                MigrationCarryOutcome.NotOnTheSameFilesystem,
                "a hard link cannot cross filesystems"));

        await Assert.ThrowsAsync<MigrationCarryRefusedException>(
            () => Passage().RunAsync(MigrationPass.ForReal, Rescanned(), Cancel));

        Assert.Equal(1, lease.Returned);
        Assert.Empty(records.Saved);
    }

    [Fact]
    public async Task ARehearsalReadsTheSameThingsAndChangesNothing()
    {
        await Passage().RunAsync(MigrationPass.Rehearsal, Rescanned(), Cancel);

        Assert.Equal(1, source.Reads);
        Assert.Equal(1, source.Walks);
        Assert.Empty(journal.Steps);
        Assert.Empty(recordings.Written);
    }

    [Fact]
    public async Task ARehearsalIsWrittenDownAsARehearsal()
    {
        await Passage().RunAsync(MigrationPass.Rehearsal, Rescanned(), Cancel);

        Assert.Equal(MigrationPass.Rehearsal, records.Saved.Single().Run.Pass);
        Assert.Equal(Source, records.Saved.Single().Run.Source);
    }

    [Fact]
    public async Task ARehearsalCanBeRunAgainAndAgain()
    {
        await Passage().RunAsync(MigrationPass.Rehearsal, Rescanned(), Cancel);
        await Passage().RunAsync(MigrationPass.Rehearsal, Rescanned(), Cancel);
        await Passage().RunAsync(MigrationPass.Rehearsal, Rescanned(), Cancel);

        Assert.Equal(3, records.Saved.Count);
        Assert.Empty(journal.Steps);
        Assert.Equal(3, lease.Returned);
    }

    [Fact]
    public async Task ARunForRealCarriesAndSaysSo()
    {
        await Passage().RunAsync(MigrationPass.ForReal, Rescanned(), Cancel);

        Assert.Equal(MigrationPass.ForReal, records.Saved.Single().Run.Pass);
        Assert.Single(recordings.Written);
        Assert.Equal(["link one.m2ts", $"row {carrier.Named.Single()}"], journal.Steps);
    }

    private MigrationPassage Passage()
        => new(
            source,
            source,
            new MigrationCarriage(carrier, recordings, clock),
            records,
            lease,
            clock);
}

internal sealed class ReadOnlySource : IMigrationSourceLedger, IMigrationSourceDirectory
{
    public int Reads { get; private set; }

    public int Walks { get; private set; }

    public Task<SourceLedger> ReadAsync(CancellationToken cancellationToken)
    {
        Reads++;

        return Task.FromResult(Ledger([Recording(7)], [AsBroadcast(7, "one.m2ts", 100)]));
    }

    public Task<IReadOnlyList<SourceFile>> ListAsync(CancellationToken cancellationToken)
    {
        Walks++;

        return Task.FromResult<IReadOnlyList<SourceFile>>([OnDisk("one.m2ts", 100)]);
    }
}

internal sealed class HeldMigrationRecords : IMigrationRecordRepository
{
    public List<MigrationReport> Saved { get; } = [];

    public Task SaveAsync(MigrationReport report, CancellationToken cancellationToken)
    {
        Saved.Add(report);

        return Task.CompletedTask;
    }

    public Task<MigrationRun?> LatestAsync(CancellationToken cancellationToken)
        => Task.FromResult(Saved.LastOrDefault()?.Run);

    public Task<MigrationReport?> ReadAsync(MigrationRunId runId, CancellationToken cancellationToken)
        => Task.FromResult(Saved.FirstOrDefault(report => report.Run.Id.Equals(runId)));
}

internal sealed class OneAtATime : IMigrationLease
{
    public bool Held { get; set; }

    public int Taken { get; private set; }

    public int Returned { get; private set; }

    public Task<IAsyncDisposable?> TakeAsync(CancellationToken cancellationToken)
    {
        if (Held)
        {
            return Task.FromResult<IAsyncDisposable?>(null);
        }

        Held = true;
        Taken++;

        return Task.FromResult<IAsyncDisposable?>(new Hold(this));
    }

    private sealed class Hold(OneAtATime lease) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            lease.Held = false;
            lease.Returned++;

            return ValueTask.CompletedTask;
        }
    }
}
