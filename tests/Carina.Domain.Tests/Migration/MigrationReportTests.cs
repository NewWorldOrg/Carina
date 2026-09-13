using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Migration;

using static Carina.Domain.Tests.Migration.MigrationFixtures;

namespace Carina.Domain.Tests.Migration;

public sealed class MigrationReportTests
{
    private static readonly MigrationRunId Another = new(new Guid("00000001-0000-0000-0000-000000000002"));

    private static IReadOnlyList<MigrationTally> Empty()
        => [.. MigrationPopulations.Counted.Select(
            population => MigrationTally.Rehydrate(Run, population, 0, 0, 0, 0))];

    [Fact]
    public void ARunThatSaysNothingAboutWhatArrivedDiminishedIsRefused()
    {
        IReadOnlyList<MigrationLoss> short_ =
            [.. Told(Run).Where(
                loss => loss.Subject is not MigrationLossSubject.DuplicateAvoidance)];

        ArgumentException refused = Assert.Throws<ArgumentException>(
            () => MigrationReport.Of(Ran(), Empty(), [], short_, Stood(Run), [], []));

        Assert.Contains("DuplicateAvoidance", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MigrationLossSubject.DuplicateAvoidance)]
    [InlineData(MigrationLossSubject.EnclosedCharacters)]
    [InlineData(MigrationLossSubject.DayBoundary)]
    public void EveryLossCarriedIntoTheNewSystemHasToBeSaidOutLoud(MigrationLossSubject subject)
    {
        IReadOnlyList<MigrationLoss> short_ =
            [.. Told(Run).Where(loss => loss.Subject != subject)];

        Assert.Throws<ArgumentException>(() => MigrationReport.Of(Ran(), Empty(), [], short_, Stood(Run), [], []));
    }

    [Fact]
    public void ARunSaysWhatOneSubjectLostOnce()
    {
        IReadOnlyList<MigrationLoss> twice = [.. Told(Run), .. Told(Run)];

        Assert.Throws<ArgumentException>(() => MigrationReport.Of(Ran(), Empty(), [], twice, Stood(Run), [], []));
    }

    [Fact]
    public void APopulationWithNoSummaryStopsTheReport()
    {
        IReadOnlyList<MigrationTally> short_ =
            [.. Empty().Where(tally => tally.Population is not MigrationPopulation.Rules)];

        Assert.Throws<MigrationUnclassifiedException>(
            () => MigrationReport.Of(Ran(), short_, [], Told(Run), Stood(Run), [], []));
    }

    [Fact]
    public void APopulationCountedTwiceStopsTheReport()
    {
        IReadOnlyList<MigrationTally> twice =
            [.. Empty(), MigrationTally.Rehydrate(Run, MigrationPopulation.Rules, 0, 0, 0, 0)];

        Assert.Throws<ArgumentException>(
            () => MigrationReport.Of(Ran(), twice, [], Told(Run), Stood(Run), [], []));
    }

    [Fact]
    public void ARunThatCouldNotExplainSomethingCannotBeWrittenDownAsIfItHad()
    {
        IReadOnlyList<MigrationTally> loose =
            [.. Empty().Where(tally => tally.Population is not MigrationPopulation.Recordings),
                MigrationTally.Rehydrate(Run, MigrationPopulation.Recordings, 1, 0, 0, 1)];

        MigrationUnclassifiedException stopped = Assert.Throws<MigrationUnclassifiedException>(
            () => MigrationReport.Of(Ran(), loose, [], Told(Run), Stood(Run), [], []));

        Assert.Contains("nobody could class", stopped.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARunThatCountedMoreRefusalsThanItWroteDownIsRefused()
    {
        IReadOnlyList<MigrationTally> counted =
            [.. Empty().Where(tally => tally.Population is not MigrationPopulation.Recordings),
                MigrationTally.Rehydrate(Run, MigrationPopulation.Recordings, 2, 0, 2, 0)];

        MigrationDetail one = MigrationDetail.Of(
            Run,
            Refused(MigrationPopulation.Recordings, "7", MigrationRefusal.ReallyEmpty));

        ArgumentException refused = Assert.Throws<ArgumentException>(
            () => MigrationReport.Of(Ran(), counted, [one], Told(Run), Stood(Run), [], []));

        Assert.Contains("written down", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARunThatWroteDownARefusalItNeverCountedIsRefused()
    {
        MigrationDetail one = MigrationDetail.Of(
            Run,
            Refused(MigrationPopulation.Recordings, "7", MigrationRefusal.ReallyEmpty));

        Assert.Throws<ArgumentException>(
            () => MigrationReport.Of(Ran(), Empty(), [one], Told(Run), Stood(Run), [], []));
    }

    [Fact]
    public void TheSameSubjectIsNeverExplainedTwice()
    {
        IReadOnlyList<MigrationTally> counted =
            [.. Empty().Where(tally => tally.Population is not MigrationPopulation.Recordings),
                MigrationTally.Rehydrate(Run, MigrationPopulation.Recordings, 2, 0, 2, 0)];

        MigrationDetail one = MigrationDetail.Of(
            Run,
            Refused(MigrationPopulation.Recordings, "7", MigrationRefusal.ReallyEmpty));
        MigrationDetail again = MigrationDetail.Of(
            Run,
            Refused(MigrationPopulation.Recordings, "7", MigrationRefusal.FileMissing));

        Assert.Throws<ArgumentException>(
            () => MigrationReport.Of(Ran(), counted, [one, again], Told(Run), Stood(Run), [], []));
    }

    [Fact]
    public void AReportCarriesTheRowsOfItsOwnRunAndNoOthers()
    {
        IReadOnlyList<MigrationTally> elsewhere =
            [.. MigrationPopulations.Counted.Select(
                population => MigrationTally.Rehydrate(Another, population, 0, 0, 0, 0))];

        Assert.Throws<ArgumentException>(
            () => MigrationReport.Of(Ran(), elsewhere, [], Told(Run), Stood(Run), [], []));

        Assert.Throws<ArgumentException>(
            () => MigrationReport.Of(Ran(), Empty(), [], Told(Another), Stood(Run), [], []));
    }

    [Fact]
    public void EveryPopulationIsEitherCountedOrKnownNotToBe()
    {
        Assert.Equal(
            MigrationPopulations.All,
            [.. MigrationPopulations.Counted, .. MigrationPopulations.NotCounted]);

        Assert.All(
            MigrationPopulations.NotCounted,
            population => Assert.Throws<ArgumentOutOfRangeException>(
                () => MigrationPopulations.Countable(population)));
    }

    [Fact]
    public void AReportOfARunThatFoundNothingIsStillAReport()
    {
        MigrationReport told = MigrationReport.Of(Ran(), Empty(), [], Told(Run), Stood(Run), [], []);

        Assert.Equal(5, told.Tallies.Count);
        Assert.Empty(told.Details);
        Assert.Equal(MigrationLossSubjects.All.Count, told.Losses.Count);
    }

    [Fact]
    public void ARunSaysWhatBecameOfEveryChannelTheSourceSystemDefined()
    {
        IReadOnlyList<MigrationTally> counted =
        [
            .. Empty().Where(tally => tally.Population is not MigrationPopulation.ChannelDefinitions),
            MigrationTally.Rehydrate(Run, MigrationPopulation.ChannelDefinitions, 2, 2, 0, 0),
        ];

        Assert.Throws<ArgumentException>(
            () => MigrationReport.Of(Ran(), counted, [], Told(Run), Stood(Run), [Proposal(1)], []));
    }

    [Fact]
    public void ARunSaysWhatBecameOfAServiceOnce()
    {
        IReadOnlyList<MigrationTally> counted =
        [
            .. Empty().Where(tally => tally.Population is not MigrationPopulation.ChannelDefinitions),
            MigrationTally.Rehydrate(Run, MigrationPopulation.ChannelDefinitions, 2, 2, 0, 0),
        ];

        Assert.Throws<ArgumentException>(
            () => MigrationReport.Of(Ran(), counted, [], Told(Run), Stood(Run), [Proposal(1), Proposal(1)], []));
    }

    [Fact]
    public void ARunSaysWhatTheSourceSystemMeantByEveryRuleItConverted()
    {
        IReadOnlyList<MigrationTally> counted =
        [
            .. Empty().Where(tally => tally.Population is not MigrationPopulation.Rules),
            MigrationTally.Rehydrate(Run, MigrationPopulation.Rules, 1, 1, 0, 0),
        ];

        Assert.Throws<ArgumentException>(() => MigrationReport.Of(Ran(), counted, [], Told(Run), Stood(Run), [], []));
    }

    private static MigrationChannelProposal Proposal(int service)
        => MigrationChannelProposal.Rehydrate(
            Run,
            new NetworkId(1),
            new ServiceId(service),
            MigrationChannelStanding.NothingAnswers,
            "a station",
            "21",
            null);

    [Fact]
    public void ARunThatDoesNotSayWhatARunForRealWouldMeetIsRefused()
    {
        IReadOnlyList<MigrationStanding> short_ =
            [.. Stood(Run).Where(standing => standing.Subject is not MigrationStandingSubject.WhereEncodesGo)];

        ArgumentException refused = Assert.Throws<ArgumentException>(
            () => MigrationReport.Of(Ran(), Empty(), [], Told(Run), short_, [], []));

        Assert.Contains("WhereEncodesGo", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARunSaysWhatItFoundAboutASubjectOnce()
    {
        IReadOnlyList<MigrationStanding> twice =
            [.. Stood(Run), .. Stood(Run).Where(
                standing => standing.Subject is MigrationStandingSubject.TheNewRoot)];

        Assert.Throws<ArgumentException>(
            () => MigrationReport.Of(Ran(), Empty(), [], Told(Run), twice, [], []));
    }

    [Fact]
    public void WhatAnotherRunFoundIsNotCarriedOnThisRunsReport()
        => Assert.Throws<ArgumentException>(
            () => MigrationReport.Of(Ran(), Empty(), [], Told(Run), Stood(Another), [], []));

    private static IReadOnlyList<MigrationStanding> Stood(MigrationRunId run)
        => MigrationStanding.EveryOne(run, MigrationRootStanding.Empty, EncodeUnaskedStanding.Settled);

    private static IReadOnlyList<MigrationLoss> Told(MigrationRunId run)
        => MigrationLoss.EveryOne(run, MigrationAftermath.Nothing);
}
