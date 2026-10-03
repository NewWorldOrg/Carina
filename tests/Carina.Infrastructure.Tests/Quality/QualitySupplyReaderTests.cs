using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests.Quality;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class QualitySupplyReaderTests(RepositoryDatabase database)
{
    private static readonly DateTime Airs = new(2026, 9, 7, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Now = Airs.AddHours(12);

    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "a recording in flight is read as two supplies, not one")]
    public async Task ARecordingInFlightIsReadAsTwoSuppliesNotOne()
    {
        await ClearAsync();
        Recording writing = await WritingAsync(7001, TimeSpan.FromMinutes(10), Airs.AddMinutes(12));

        IReadOnlyList<SupplyReading> read = await ReadAsync();

        Assert.Equal(
            [SupplySilence.RecordingProgress, SupplySilence.RecordingMeasurement],
            read.Select(reading => reading.Silence));
        Assert.All(read, reading => Assert.Equal(writing.Id.Wire, reading.Subject.Key));
        Assert.All(read, reading => Assert.Null(reading.Allowed));
        Assert.Equal(Airs.AddMinutes(10), read[0].LastHeardAt);
        Assert.Equal(Airs.AddMinutes(12), read[1].LastHeardAt);
    }

    [Fact(DisplayName = "a recording nothing has measured yet is heard from when it began")]
    public async Task ARecordingNothingHasMeasuredYetIsHeardFromWhenItBegan()
    {
        await ClearAsync();
        await WritingAsync(7011, TimeSpan.Zero, null);

        IReadOnlyList<SupplyReading> read = await ReadAsync();

        Assert.All(read, reading => Assert.Equal(Airs, reading.LastHeardAt));
    }

    [Fact(DisplayName = "a recording that has ended is not a supply that went quiet")]
    public async Task ARecordingThatHasEndedIsNotASupplyThatWentQuiet()
    {
        await ClearAsync();
        await WritingAsync(
            7021,
            TimeSpan.FromMinutes(30),
            Airs.AddMinutes(30),
            recording =>
            {
                recording.Abort(Airs.AddMinutes(30));
                recording.Settle(RecordingOutcome.Complete, 1, Airs.AddMinutes(30));
            });

        Assert.Empty(await ReadAsync());
    }

    [Fact(DisplayName = "the visit ledger is heard from when the back-off says the first visit is due again")]
    public async Task TheVisitLedgerIsHeardFromWhenTheBackOffSaysTheFirstVisitIsDueAgain()
    {
        await ClearAsync();
        await VisitedAsync(VisitOutcome.Complete, Airs, 32_736);

        SupplyReading read = Assert.Single(await ReadAsync());

        Assert.Equal(SupplySilence.GuideVisits, read.Silence);
        Assert.Equal(QualitySubject.TheGuideLedger, read.Subject);
        Assert.Equal(Airs + new CollectionSettings().BetweenVisits, read.LastHeardAt);
    }

    [Fact(DisplayName = "the whole visit ledger is one supply rather than one for each stream")]
    public async Task TheWholeVisitLedgerIsOneSupplyRatherThanOneForEachStream()
    {
        await ClearAsync();
        await VisitedAsync(VisitOutcome.Complete, Airs, 32_736);
        await VisitedAsync(VisitOutcome.Complete, Airs.AddMinutes(1), 32_737);
        await VisitedAsync(VisitOutcome.Complete, Airs.AddMinutes(2), 32_738);

        Assert.Single(await ReadAsync());
    }

    [Fact(DisplayName = "a ledger with a visit overdue is not quiet while the next sweep has yet to come round")]
    public async Task ALedgerWithAVisitOverdueIsNotQuietWhileTheNextSweepHasYetToComeRound()
    {
        await ClearAsync();
        await VisitedAsync(VisitOutcome.Complete, Airs, 32_736);
        await VisitedAsync(VisitOutcome.Complete, Now - (FiveMinutes * 2), 32_737);

        SupplyReading read = Assert.Single(await ReadAsync());

        Assert.Equal(Now - (FiveMinutes * 2), read.LastHeardAt);
        Assert.Empty(SupplyWatch.Quiet([read], FiveMinutes, Now));
    }

    [Fact(DisplayName = "a ledger with a visit overdue and nothing attempted for as long as a sweep takes to come round is quiet")]
    public async Task ALedgerWithAVisitOverdueAndNothingAttemptedForAsLongAsASweepTakesToComeRoundIsQuiet()
    {
        TimeSpan round = new CollectionSettings().LongestBetweenAttempts();

        await ClearAsync();
        await VisitedAsync(VisitOutcome.Complete, Airs, 32_736);
        await VisitedAsync(VisitOutcome.Complete, Now - round, 32_737);

        SupplyReading read = Assert.Single(await ReadAsync());
        SupplySilenceFinding found = Assert.Single(SupplyWatch.Quiet([read], FiveMinutes, Now));

        Assert.Equal(Now - round, read.LastHeardAt);
        Assert.Equal(round, found.Allowed);
    }

    [Fact(DisplayName = "a ledger whose overdue visits full tuners turn away sweep after sweep is not quiet")]
    public async Task ALedgerWhoseOverdueVisitsFullTunersTurnAwaySweepAfterSweepIsNotQuiet()
    {
        CollectionSettings settings = new();
        DateTime turnedAway = Now - settings.BetweenSweeps - settings.WhenTunersAreFull.WaitBeforeTheCeiling();

        await ClearAsync();
        await VisitedAsync(VisitOutcome.Complete, Airs, 32_736);
        await VisitedAsync(VisitOutcome.Interrupted, turnedAway, 32_737);

        SupplyReading read = Assert.Single(await ReadAsync());

        Assert.Equal(turnedAway, read.LastHeardAt);
        Assert.Empty(SupplyWatch.Quiet([read], FiveMinutes, Now));
    }

    [Fact(DisplayName = "a ledger that went quiet is heard from again once something is attempted on it")]
    public async Task ALedgerThatWentQuietIsHeardFromAgainOnceSomethingIsAttemptedOnIt()
    {
        await ClearAsync();
        await VisitedAsync(VisitOutcome.Complete, Airs, 32_736);
        await VisitedAsync(VisitOutcome.Complete, Airs.AddMinutes(1), 32_737);

        Assert.Single(SupplyWatch.Quiet(await ReadAsync(), FiveMinutes, Now));

        await VisitedAsync(VisitOutcome.Complete, Now - TimeSpan.FromMinutes(1), 32_736);

        Assert.Empty(SupplyWatch.Quiet(await ReadAsync(), FiveMinutes, Now));
    }

    [Fact(DisplayName = "how long the visit ledger may stay quiet follows the collection settings rather than a number of its own")]
    public async Task HowLongTheVisitLedgerMayStayQuietFollowsTheCollectionSettings()
    {
        CollectionSettings hurried = new()
        {
            BetweenSweeps = FiveMinutes,
            LongestVisit = TimeSpan.FromMinutes(1),
            WhenTunersAreFull = new RotationBackoff(TimeSpan.FromSeconds(10), 2, TimeSpan.FromMinutes(1), 2),
        };

        await ClearAsync();
        await VisitedAsync(VisitOutcome.Complete, Airs, 32_736);
        await VisitedAsync(VisitOutcome.Complete, Now - (FiveMinutes * 2), 32_737);

        SupplyReading read = Assert.Single(await ReadAsync(hurried));

        Assert.Equal(TimeSpan.FromSeconds(300 + 10 + 60 + 60), read.Allowed);
        Assert.Single(SupplyWatch.Quiet([read], FiveMinutes, Now));
    }

    [Fact(DisplayName = "a ledger whose visits full tuners all turned away is heard from when the last was turned away")]
    public async Task ALedgerWhoseVisitsFullTunersAllTurnedAwayIsHeardFromWhenTheLastWasTurnedAway()
    {
        await ClearAsync();
        await VisitedAsync(VisitOutcome.Interrupted, Now - FiveMinutes, 61);
        await VisitedAsync(VisitOutcome.Interrupted, Now - FiveMinutes + TimeSpan.FromSeconds(1), 62);

        SupplyReading read = Assert.Single(await ReadAsync());

        Assert.Equal(Now - FiveMinutes + TimeSpan.FromSeconds(1), read.LastHeardAt);
        Assert.Empty(SupplyWatch.Quiet([read], FiveMinutes, Now));
    }

    [Fact(DisplayName = "a ledger whose visits were all turned away and then nothing was attempted for a sweep's round is quiet")]
    public async Task ALedgerWhoseVisitsWereAllTurnedAwayAndThenNothingWasAttemptedIsQuiet()
    {
        TimeSpan round = new CollectionSettings().LongestBetweenAttempts();

        await ClearAsync();
        await VisitedAsync(VisitOutcome.Interrupted, Now - round - TimeSpan.FromSeconds(1), 61);
        await VisitedAsync(VisitOutcome.Interrupted, Now - round, 62);

        Assert.Single(SupplyWatch.Quiet(await ReadAsync(), FiveMinutes, Now));
    }

    private async Task<IReadOnlyList<SupplyReading>> ReadAsync(CollectionSettings? settings = null)
    {
        await using CarinaDbContext reading = database.Open();

        return await new QualitySupplyReader(reading, settings ?? new CollectionSettings()).ReadAsync(Cancel);
    }

    private async Task ClearAsync()
    {
        await using CarinaDbContext clearing = database.Open();
        await clearing.Set<Recording>().ExecuteDeleteAsync(Cancel);
        await clearing.Set<StreamVisit>().ExecuteDeleteAsync(Cancel);
    }

    private async Task VisitedAsync(VisitOutcome outcome, DateTime at, int stream)
    {
        await using CarinaDbContext writing = database.Open();

        await new StreamVisitRepository(writing).SaveAsync(
            StreamVisit.Record(
                new NetworkId(32_736),
                new TransportStreamId(stream),
                outcome,
                at,
                TimeSpan.FromSeconds(1)),
            Cancel);
    }

    private async Task<Recording> WritingAsync(
        int eventId,
        TimeSpan written,
        DateTime? measuredAt,
        Action<Recording>? ending = null)
    {
        RecordingId id = RecordingId.New();
        Recording begun = Recording.Begin(
            id,
            ReservationId.New(),
            new ProgrammeRef(new NetworkId(32_736), new ServiceId(1_024), new EventId(eventId), Airs),
            new OutputRoot("primary"),
            RecordingFileName.For(id, ".ts"),
            Airs,
            Airs.AddMinutes(30),
            new ProgrammeSnapshot(
                "A programme",
                string.Empty,
                string.Empty,
                [],
                Airs.AddHours(-6),
                AudioMode.Undetermined,
                ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            Airs,
            new TunerDeviceId("adapter3.frontend0"));

        if (written > TimeSpan.Zero)
        {
            begun.Wrote(written);
        }

        if (measuredAt is { } measured)
        {
            begun.Measure(DropCounters.Counted(0, 1), DropTimeline.Unlocated, 0, 0, measured);
        }

        ending?.Invoke(begun);

        await using CarinaDbContext holding = database.Open();
        await new RecordingRepository(holding).AddAsync(begun, Cancel);

        return begun;
    }
}
