using Carina.Domain.Encodings;
using Carina.Domain.Migration;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Migration;
using Carina.TestSupport;

using static Carina.Infrastructure.Tests.Migration.CarriedMigrationFixtures;

namespace Carina.Infrastructure.Tests.Migration;

public sealed class MigrationCarriageTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly MigrationRunId Run = new(new Guid("00000001-0000-0000-0000-000000000001"));

    private readonly HandTurnedClock clock = new(new DateTimeOffset(2026, 9, 8, 5, 0, 0, TimeSpan.Zero));

    private readonly MigrationBench bench;

    private readonly MigrationJournal journal;

    private readonly ScriptedCarrier carrier;

    private readonly HeldMigratedRecordings recordings;

    public MigrationCarriageTests()
    {
        bench = new MigrationBench(clock);
        journal = bench.Journal;
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

        Assert.Contains("take out of the new root", stopped.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("delete the new root", stopped.Message, StringComparison.Ordinal);
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
    public async Task ARehearsalSaysWhatARunForRealWouldMeetInsteadOfRefusing()
    {
        carrier.Standing = MigrationRootStanding.NotEmpty;
        carrier.WouldCarry = MigrationCarryStanding.WouldCrossAMount;
        bench.Destinations.Destinations.Add(EncodeDestination.Define(
            EncodeDestinationId.New(),
            new EncodeLabel("Elsewhere"),
            new OutputRoot("elsewhere"),
            bench.Profile.Id,
            Began));

        MigrationCarried carried = await CarriedAsync(MigrationPass.Rehearsal);

        Assert.Equal(MigrationRootStanding.NotEmpty, carried.NewRoot);
        Assert.Equal(EncodeUnaskedStanding.MoreThanOneIsOffered, carried.WhereEncodesGo);
        Assert.Equal(MigrationCarryStanding.WouldCrossAMount, carried.Carrying);
        Assert.Empty(journal.Steps);
        Assert.Empty(recordings.Written);
    }

    [Fact]
    public async Task ARehearsalThatWouldMeetNothingInTheWaySaysSo()
    {
        MigrationCarried carried = await CarriedAsync(MigrationPass.Rehearsal);

        Assert.Equal(MigrationRootStanding.Empty, carried.NewRoot);
        Assert.Equal(EncodeUnaskedStanding.Settled, carried.WhereEncodesGo);
        Assert.Equal(MigrationCarryStanding.WouldBeAHardLink, carried.Carrying);
        Assert.Empty(journal.Steps);
        Assert.Empty(recordings.Written);
    }

    [Fact]
    public async Task ARehearsalLooksAtTheNewRootTooRatherThanLeavingTheQuestionToTheRunForReal()
    {
        carrier.Standing = MigrationRootStanding.Missing;

        MigrationCarried carried = await CarriedAsync(MigrationPass.Rehearsal);

        Assert.Equal(MigrationRootStanding.Missing, carried.NewRoot);
        Assert.Empty(journal.Steps);
        Assert.Empty(recordings.Written);
    }

    [Theory]
    [InlineData(MigrationCarryStanding.WouldCrossAMount)]
    [InlineData(MigrationCarryStanding.TheNewRootDoesNotTakeALink)]
    public async Task ARehearsalSaysWhatALinkIntoTheNewRootWouldMeetInsteadOfRefusing(
        MigrationCarryStanding standing)
    {
        carrier.WouldCarry = standing;

        MigrationCarried carried = await CarriedAsync(MigrationPass.Rehearsal);

        Assert.Equal(standing, carried.Carrying);
        Assert.Empty(journal.Steps);
        Assert.Empty(recordings.Written);
    }

    [Theory]
    [InlineData(MigrationCarryStanding.WouldCrossAMount)]
    [InlineData(MigrationCarryStanding.TheNewRootDoesNotTakeALink)]
    public async Task ARunForRealStopsBeforeTheFirstLinkWhenNoLinkCouldBeMade(MigrationCarryStanding standing)
    {
        carrier.WouldCarry = standing;

        MigrationCarryRefusedException stopped = await Assert.ThrowsAsync<MigrationCarryRefusedException>(
            () => CarryAsync(MigrationPass.ForReal));

        Assert.Contains("Nothing was carried", stopped.Message, StringComparison.Ordinal);
        Assert.Empty(journal.Steps);
        Assert.Empty(recordings.Written);
    }

    [Fact]
    public async Task ARunForRealIsNotStoppedByASourceThatHasNothingLeftToCarry()
    {
        carrier.WouldCarry = MigrationCarryStanding.NothingIsThereToCarry;

        MigrationCarried carried = await CarriedAsync(MigrationPass.ForReal);

        Assert.Equal(MigrationCarryStanding.NothingIsThereToCarry, carried.Carrying);
        Assert.NotEmpty(recordings.Written);
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
    public async Task WhatArrivesWasPromisedTheEndItArrivesWith()
    {
        await CarryAsync(MigrationPass.ForReal);

        Recording written = recordings.Written.Single();

        Assert.Equal(written.ExpectedWindowEnd, written.PromisedWindowEnd);
        Assert.Equal(written.StoppedAtActual, written.PromisedWindowEnd);
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

        MigrationRoll settled = (await bench.Carriage(carrier, recordings).CarryAsync(
            Run,
            ledger,
            Rescanned(),
            Rolled(ledger, [OnDisk("one.m2ts", 100)]),
            MigrationPass.ForReal,
            Cancel)).Roll;

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

    [Fact]
    public async Task EveryRuleThatCrossesOverArrivesTurnedOff()
    {
        SourceLedger ledger = Ledger(
            rules: [Rule(3, enabled: true), Rule(4, enabled: false)],
            channels: []);

        await CarriedAsync(MigrationPass.ForReal, ledger);

        Assert.Equal(2, bench.Rules.Rules.Count);
        Assert.All(bench.Rules.Rules, rule => Assert.False(rule.Enabled));
    }

    [Fact]
    public async Task WhatTheSourceSystemMeantByARuleIsWrittenDownEvenThoughTheRuleArrivesTurnedOff()
    {
        SourceLedger ledger = Ledger(rules: [Rule(3, enabled: true), Rule(4, enabled: false)]);

        MigrationCarried carried = await CarriedAsync(MigrationPass.ForReal, ledger);

        Assert.Equal([3L, 4L], carried.Aftermath.RuleProposals.Select(proposal => proposal.SourceRow).Order());
        Assert.True(carried.Aftermath.RuleProposals.Single(proposal => proposal.SourceRow is 3).EnabledAtTheSource);
        Assert.False(carried.Aftermath.RuleProposals.Single(proposal => proposal.SourceRow is 4).EnabledAtTheSource);
        Assert.All(
            carried.Aftermath.RuleProposals,
            proposal => Assert.Contains(bench.Rules.Rules, rule => rule.Id.Equals(proposal.RuleId)));
    }

    [Fact]
    public async Task TheRulesThatCrossedOverStillNarrowedToDaysAreCountedAmongWhatArrivedDiminished()
    {
        SourceLedger ledger = Ledger(rules:
        [
            Rule(3, days: 0b000_0001),
            Rule(4, days: 0b100_0001),
            Rule(5, days: SourceWeek.EveryDay),
        ]);

        MigrationCarried carried = await CarriedAsync(MigrationPass.ForReal, ledger);

        Assert.Equal(2, carried.Aftermath.RulesNarrowedByDay);
    }

    [Fact]
    public async Task ARehearsalMakesNoRuleAndStillSaysWhatItWouldHaveMade()
    {
        SourceLedger ledger = Ledger(rules: [Rule(3)]);

        MigrationCarried carried = await CarriedAsync(MigrationPass.Rehearsal, ledger);

        Assert.Empty(bench.Rules.Rules);
        Assert.Null(Assert.Single(carried.Aftermath.RuleProposals).RuleId);
    }

    [Fact]
    public async Task ARuleTheSourceSystemWroteInAShapeThisSystemCannotTakeIsNeverMade()
    {
        SourceRule refused = new(
            3,
            "hill",
            true,
            new SourceRuleTerms(
                "hill",
                string.Empty,
                SourceRuleFields.Title | SourceRuleFields.ExtendedBody,
                SourceRuleFields.Title | SourceRuleFields.ExtendedBody,
                [],
                [],
                [],
                SourceWeek.EveryDay),
            SourceRuleReach.Plain);

        MigrationCarried carried = await CarriedAsync(MigrationPass.ForReal, Ledger(rules: [refused]));

        Assert.Empty(bench.Rules.Rules);
        Assert.Empty(carried.Aftermath.RuleProposals);
        Assert.Equal(
            MigrationRefusal.NoSuchFeature,
            carried.Roll.Verdicts.Single(verdict => verdict.Population is MigrationPopulation.Rules).Refusal);
    }

    [Fact]
    public async Task EveryRecordingThatCrossesOverIsQueuedForEncodingOneJobAtATime()
    {
        await CarryAsync(MigrationPass.ForReal);

        EncodeJob queued = Assert.Single(bench.Jobs.Jobs);

        Assert.Equal(recordings.Written.Single().Id, queued.RecordingId);
        Assert.Equal(EncodeJobStatus.Queued, queued.Status);
        Assert.Equal(bench.Profile.Id, queued.ProfileId);
        Assert.Equal(bench.Destination.Id, queued.DestinationId);
        Assert.Equal(bench.Destination.OutputRoot, queued.OutputRoot);
    }

    [Fact]
    public async Task ARehearsalQueuesNothingForEncoding()
    {
        await CarryAsync(MigrationPass.Rehearsal);

        Assert.Empty(bench.Jobs.Jobs);
    }

    [Fact]
    public async Task ARunForRealStopsBeforeItCarriesAnythingWhenNothingSaysWhereEncodesGo()
    {
        bench.Destinations.Destinations.Clear();

        MigrationCarryRefusedException stopped = await Assert.ThrowsAsync<MigrationCarryRefusedException>(
            () => CarryAsync(MigrationPass.ForReal));

        Assert.Contains("exactly one destination", stopped.Message, StringComparison.Ordinal);
        Assert.Empty(journal.Steps);
        Assert.Empty(recordings.Written);
    }

    [Fact]
    public async Task ARunForRealStopsWhenMoreThanOneDestinationIsOfferedBecauseItDoesNotGuess()
    {
        bench.Destinations.Destinations.Add(EncodeDestination.Define(
            EncodeDestinationId.New(),
            new EncodeLabel("Elsewhere"),
            new OutputRoot("elsewhere"),
            bench.Profile.Id,
            Began));

        await Assert.ThrowsAsync<MigrationCarryRefusedException>(() => CarryAsync(MigrationPass.ForReal));

        Assert.Empty(recordings.Written);
    }

    [Fact]
    public async Task WhatBecameOfEveryChannelTheSourceSystemDefinedIsWrittenDown()
    {
        SourceLedger ledger = Ledger(channels: [Channel(11, InReach, "21"), Channel(12, Elsewhere, "27")]);

        MigrationCarried carried = await CarriedAsync(MigrationPass.ForReal, ledger);

        Assert.Equal(2, carried.Aftermath.ChannelProposals.Count);
        Assert.Equal(
            MigrationChannelStanding.NameProposed,
            carried.Aftermath.ChannelProposals.Single(proposal => proposal.SourcePhysicalChannel is "21").Standing);
        Assert.Equal(
            MigrationChannelStanding.NothingAnswers,
            carried.Aftermath.ChannelProposals.Single(proposal => proposal.SourcePhysicalChannel is "27").Standing);
    }

    [Fact]
    public async Task OnlyWhatWasCarriedIsCountedAmongWhatArrivedDiminished()
    {
        var ledger = new SourceLedger(
            Source,
            [
                new SourceRecording(7, "an evening walk [字]", Began, Ended, InReach, new EventId(4321)),
                new SourceRecording(8, "a morning walk [字]", Began, Ended, InReach, null),
            ],
            [AsBroadcast(7, "one.m2ts", 100), AsBroadcast(8, "two.m2ts", 100)],
            [Rule(3, keyword: "hill [字]"), Refused(4, "moor [字]")],
            [new SourceReservation(5, "a night walk [字]", false)],
            [new SourceChannelDefinition(11, "a station [字]", SourceBroadcastKind.Terrestrial, InReach, "21")]);

        MigrationCarried carried = await bench.Carriage(carrier, recordings).CarryAsync(
            Run,
            ledger,
            Rescanned(),
            Rolled(ledger, [OnDisk("one.m2ts", 100), OnDisk("two.m2ts", 100)]),
            MigrationPass.ForReal,
            Cancel);

        Assert.Equal(1, carried.Aftermath.RulesRead);
        Assert.Equal(2, carried.Aftermath.RowsPastRestoring);
    }

    private async Task<MigrationRoll> CarryAsync(MigrationPass pass) => (await CarriedAsync(pass)).Roll;

    private static SourceRule Refused(long id, string name)
        => new(
            id,
            name,
            true,
            new SourceRuleTerms(
                name,
                string.Empty,
                SourceRuleFields.Title | SourceRuleFields.ExtendedBody,
                SourceRuleFields.Title | SourceRuleFields.ExtendedBody,
                [],
                [],
                [],
                SourceWeek.EveryDay),
            SourceRuleReach.Plain);

    private Task<MigrationCarried> CarriedAsync(MigrationPass pass, SourceLedger? ledger = null)
    {
        SourceLedger read = ledger ?? Ledger([Recording(7)], [AsBroadcast(7, "one.m2ts", 100)]);

        return bench.Carriage(carrier, recordings).CarryAsync(
            Run,
            read,
            Rescanned(),
            Rolled(read, [OnDisk("one.m2ts", 100)]),
            pass,
            Cancel);
    }
}
