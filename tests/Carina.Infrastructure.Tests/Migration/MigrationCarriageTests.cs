using Carina.Domain.Migration;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Migration;
using Carina.TestSupport;

using static Carina.Infrastructure.Tests.Migration.CarriedMigrationFixtures;

namespace Carina.Infrastructure.Tests.Migration;

public sealed class MigrationCarriageTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly MigrationJournal journal = new();

    private readonly HandTurnedClock clock = new(new DateTimeOffset(2026, 9, 8, 5, 0, 0, TimeSpan.Zero));

    private readonly ScriptedCarrier carrier;

    private readonly HeldMigratedRecordings recordings;

    public MigrationCarriageTests()
    {
        carrier = new ScriptedCarrier(journal);
        recordings = new HeldMigratedRecordings(journal);
    }

    [Fact]
    public async Task TheLinkIsFinishedBeforeTheRowIsWritten()
    {
        await CarryAsync(MigrationPass.ForReal);

        Assert.Equal(["link one.m2ts", $"row {carrier.Named.Single()}"], journal.Steps);
    }

    [Fact]
    public async Task ARowIsNeverWrittenForALinkThatWasNotMade()
    {
        carrier.Answers(
            "one.m2ts",
            new MigrationCarry(MigrationCarryOutcome.SourceGone, "the file the ledger names is not there"));

        MigrationRoll settled = await CarryAsync(MigrationPass.ForReal);

        Assert.Empty(recordings.Written);
        Assert.Equal(
            MigrationRefusal.FileMissing,
            settled.Verdicts.Single(verdict => verdict.Population is MigrationPopulation.Recordings).Refusal);
    }

    [Fact]
    public async Task ARunStopsRatherThanCopyingWhenTheNewRootIsOnAnotherFilesystem()
    {
        carrier.Answers(
            "one.m2ts",
            new MigrationCarry(
                MigrationCarryOutcome.NotOnTheSameFilesystem,
                "a hard link cannot cross filesystems"));

        MigrationCarryRefusedException stopped = await Assert.ThrowsAsync<MigrationCarryRefusedException>(
            () => CarryAsync(MigrationPass.ForReal));

        Assert.Contains("cross filesystems", stopped.Message, StringComparison.Ordinal);
        Assert.Empty(recordings.Written);
    }

    [Fact]
    public async Task ARunStopsWhenSomethingIsAlreadyUnderTheNameItWouldUse()
    {
        carrier.Answers(
            "one.m2ts",
            new MigrationCarry(MigrationCarryOutcome.AlreadyThere, "something is already there"));

        await Assert.ThrowsAsync<MigrationCarryRefusedException>(() => CarryAsync(MigrationPass.ForReal));

        Assert.Empty(recordings.Written);
    }

    [Fact]
    public async Task ARunForRealNeverAddsToANewRootThatAlreadyHoldsSomething()
    {
        carrier.Standing = MigrationRootStanding.NotEmpty;

        MigrationCarryRefusedException stopped = await Assert.ThrowsAsync<MigrationCarryRefusedException>(
            () => CarryAsync(MigrationPass.ForReal));

        Assert.Contains("delete the new root", stopped.Message, StringComparison.Ordinal);
        Assert.Empty(journal.Steps);
        Assert.Empty(recordings.Written);
    }

    [Fact]
    public async Task ARehearsalIsRunEvenWhenTheNewRootStillHoldsWhatALastRunLeft()
    {
        carrier.Standing = MigrationRootStanding.NotEmpty;

        MigrationRoll settled = await CarryAsync(MigrationPass.Rehearsal);

        Assert.True(settled.Verdicts.Single(verdict => verdict.Population is MigrationPopulation.Recordings).Carried);
    }

    [Fact]
    public async Task ARehearsalLinksNothingAndWritesNoRow()
    {
        MigrationRoll settled = await CarryAsync(MigrationPass.Rehearsal);

        Assert.Empty(journal.Steps);
        Assert.Empty(recordings.Written);
        Assert.True(settled.Verdicts.Single(verdict => verdict.Population is MigrationPopulation.Recordings).Carried);
    }

    [Fact]
    public async Task WhatArrivesIsUnmeasuredAndCarriesNoMemoryOfWhereItCameFrom()
    {
        await CarryAsync(MigrationPass.ForReal);

        Recording written = recordings.Written.Single();

        Assert.False(written.CcMeasured);
        Assert.Null(written.CcDroppedPackets);
        Assert.Null(written.CcTotalPackets);
        Assert.Null(written.MeasuredUpdatedAt);
        Assert.Null(written.TunerDeviceId);
        Assert.Null(written.ReservationId);
        Assert.Equal(ThumbnailState.Pending, written.ThumbnailState);
    }

    [Fact]
    public async Task TheNameItArrivesUnderIsTheOneTheNewSystemWouldHaveGivenIt()
    {
        await CarryAsync(MigrationPass.ForReal);

        Recording written = recordings.Written.Single();

        Assert.True(written.FileName.Names(written.Id));
        Assert.Equal(Root, written.OutputRoot);
        Assert.Equal(carrier.Named.Single(), written.FileName.Value);
    }

    [Fact]
    public async Task ARecordingWhoseProgrammeTheSourceCannotNameIsNeverCarried()
    {
        SourceLedger ledger = Ledger(
            [RecordingOfNoProgramme(7)],
            [AsBroadcast(7, "one.m2ts", 100)]);

        MigrationRoll settled = await new MigrationCarriage(carrier, recordings, clock).CarryAsync(
            ledger,
            Rolled(ledger, [OnDisk("one.m2ts", 100)]),
            MigrationPass.ForReal,
            Cancel);

        Assert.Empty(recordings.Written);
        Assert.Equal(
            MigrationRefusal.Unidentifiable,
            settled.Verdicts.Single(verdict => verdict.Population is MigrationPopulation.Recordings).Refusal);
    }

    [Fact]
    public async Task WhatWasCarriedStillAddsUpToWhatWasOffered()
    {
        MigrationRoll settled = await CarryAsync(MigrationPass.ForReal);

        Assert.Equal(MigrationPopulations.Counted, settled.Populations);
        Assert.Equal(1, settled.OfferedIn(MigrationPopulation.Recordings));
        Assert.Equal(1, settled.OfferedIn(MigrationPopulation.RecordingFiles));
    }

    private Task<MigrationRoll> CarryAsync(MigrationPass pass)
    {
        SourceLedger ledger = Ledger([Recording(7)], [AsBroadcast(7, "one.m2ts", 100)]);

        return new MigrationCarriage(carrier, recordings, clock).CarryAsync(
            ledger,
            Rolled(ledger, [OnDisk("one.m2ts", 100)]),
            pass,
            Cancel);
    }
}
