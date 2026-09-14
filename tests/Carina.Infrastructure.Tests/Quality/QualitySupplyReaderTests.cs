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

    [Fact(DisplayName = "BR-QD-007: a recording in flight is read as two supplies, not one")]
    public async Task ARecordingInFlightIsReadAsTwoSuppliesNotOne()
    {
        await ClearAsync();
        Recording writing = await WritingAsync(7001, TimeSpan.FromMinutes(10), Airs.AddMinutes(12));

        IReadOnlyList<SupplyReading> read = await ReadAsync();

        Assert.Equal(
            [SupplySilence.RecordingProgress, SupplySilence.RecordingMeasurement],
            read.Select(reading => reading.Silence));
        Assert.All(read, reading => Assert.Equal(writing.Id.Wire, reading.Subject.Key));
        Assert.Equal(Airs.AddMinutes(10), read[0].LastHeardAt);
        Assert.Equal(Airs.AddMinutes(12), read[1].LastHeardAt);
    }

    [Fact(DisplayName = "BR-QD-007: a recording nothing has measured yet is heard from when it began")]
    public async Task ARecordingNothingHasMeasuredYetIsHeardFromWhenItBegan()
    {
        await ClearAsync();
        await WritingAsync(7011, TimeSpan.Zero, null);

        IReadOnlyList<SupplyReading> read = await ReadAsync();

        Assert.All(read, reading => Assert.Equal(Airs, reading.LastHeardAt));
    }

    [Fact(DisplayName = "BR-QD-007: a recording that has ended is not a supply that went quiet")]
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

    [Fact(DisplayName = "BR-QD-007: the visit ledger is heard from when the back-off says the first visit is due again")]
    public async Task TheVisitLedgerIsHeardFromWhenTheBackOffSaysTheFirstVisitIsDueAgain()
    {
        await ClearAsync();
        await VisitedAsync(VisitOutcome.Complete, Airs, 32_736);

        SupplyReading read = Assert.Single(await ReadAsync());

        Assert.Equal(SupplySilence.GuideVisits, read.Silence);
        Assert.Equal(QualitySubject.TheGuideLedger, read.Subject);
        Assert.Equal(Airs + new CollectionSettings().BetweenVisits, read.LastHeardAt);
    }

    [Fact(DisplayName = "BR-QD-007: the whole visit ledger is one supply rather than one for each stream")]
    public async Task TheWholeVisitLedgerIsOneSupplyRatherThanOneForEachStream()
    {
        await ClearAsync();
        await VisitedAsync(VisitOutcome.Complete, Airs, 32_736);
        await VisitedAsync(VisitOutcome.Complete, Airs.AddMinutes(1), 32_737);
        await VisitedAsync(VisitOutcome.Complete, Airs.AddMinutes(2), 32_738);

        Assert.Single(await ReadAsync());
    }

    [Fact(DisplayName = "BR-QD-007: a ledger something was attempted on within the threshold is not quiet, overdue visit or not")]
    public async Task ALedgerSomethingWasAttemptedOnWithinTheThresholdIsNotQuiet()
    {
        await ClearAsync();
        await VisitedAsync(VisitOutcome.Complete, Airs, 32_736);
        await VisitedAsync(VisitOutcome.Complete, Now - TimeSpan.FromMinutes(1), 32_737);

        SupplyReading read = Assert.Single(await ReadAsync());

        Assert.Equal(Now - TimeSpan.FromMinutes(1), read.LastHeardAt);
        Assert.Empty(SupplyWatch.Quiet([read], FiveMinutes, Now));
    }

    [Fact(DisplayName = "BR-QD-007: a ledger with a visit overdue and nothing attempted for longer than the threshold is quiet")]
    public async Task ALedgerWithAVisitOverdueAndNothingAttemptedForLongerThanTheThresholdIsQuiet()
    {
        await ClearAsync();
        await VisitedAsync(VisitOutcome.Complete, Airs, 32_736);
        await VisitedAsync(VisitOutcome.Complete, Now - TimeSpan.FromMinutes(10), 32_737);

        SupplyReading read = Assert.Single(await ReadAsync());

        Assert.Equal(Now - TimeSpan.FromMinutes(10), read.LastHeardAt);
        Assert.Single(SupplyWatch.Quiet([read], FiveMinutes, Now));
    }

    [Fact(DisplayName = "BR-QD-007: a ledger whose visits the sweep all broke off is not one the back-off has made due")]
    public async Task ALedgerWhoseVisitsTheSweepAllBrokeOffIsNotOneTheBackOffHasMadeDue()
    {
        await ClearAsync();
        await VisitedAsync(VisitOutcome.Interrupted, Airs, 32_736);
        await VisitedAsync(VisitOutcome.Interrupted, Airs.AddMinutes(1), 32_737);

        Assert.Empty(await ReadAsync());
    }

    private async Task<IReadOnlyList<SupplyReading>> ReadAsync()
    {
        await using CarinaDbContext reading = database.Open();

        return await new QualitySupplyReader(reading, new CollectionSettings()).ReadAsync(Cancel);
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
