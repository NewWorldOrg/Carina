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
    public void ARunThatSaysNothingAboutWhatItDidNotDoIsRefused()
    {
        IReadOnlyList<MigrationOmission> short_ =
            [.. MigrationOmission.EveryOne(Run).Where(
                omission => omission.Subject is not MigrationOmissionSubject.ProgrammeGuide)];

        ArgumentException refused = Assert.Throws<ArgumentException>(
            () => MigrationReport.Of(Ran(), Empty(), [], short_));

        Assert.Contains("ProgrammeGuide", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MigrationOmissionSubject.ProgrammeGuide)]
    [InlineData(MigrationOmissionSubject.DuplicateAvoidance)]
    [InlineData(MigrationOmissionSubject.QualityTimeSeries)]
    [InlineData(MigrationOmissionSubject.RecordingHistory)]
    public void EachThingDeliberatelyLeftAloneHasToBeSaidOutLoud(MigrationOmissionSubject subject)
    {
        IReadOnlyList<MigrationOmission> short_ =
            [.. MigrationOmission.EveryOne(Run).Where(omission => omission.Subject != subject)];

        Assert.Throws<ArgumentException>(() => MigrationReport.Of(Ran(), Empty(), [], short_));
    }

    [Fact]
    public void APopulationWithNoSummaryStopsTheReport()
    {
        IReadOnlyList<MigrationTally> short_ =
            [.. Empty().Where(tally => tally.Population is not MigrationPopulation.Rules)];

        Assert.Throws<MigrationUnclassifiedException>(
            () => MigrationReport.Of(Ran(), short_, [], MigrationOmission.EveryOne(Run)));
    }

    [Fact]
    public void APopulationCountedTwiceStopsTheReport()
    {
        IReadOnlyList<MigrationTally> twice =
            [.. Empty(), MigrationTally.Rehydrate(Run, MigrationPopulation.Rules, 0, 0, 0, 0)];

        Assert.Throws<ArgumentException>(
            () => MigrationReport.Of(Ran(), twice, [], MigrationOmission.EveryOne(Run)));
    }

    [Fact]
    public void ARunThatCouldNotExplainSomethingCannotBeWrittenDownAsIfItHad()
    {
        IReadOnlyList<MigrationTally> loose =
            [.. Empty().Where(tally => tally.Population is not MigrationPopulation.Recordings),
                MigrationTally.Rehydrate(Run, MigrationPopulation.Recordings, 1, 0, 0, 1)];

        MigrationUnclassifiedException stopped = Assert.Throws<MigrationUnclassifiedException>(
            () => MigrationReport.Of(Ran(), loose, [], MigrationOmission.EveryOne(Run)));

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
            () => MigrationReport.Of(Ran(), counted, [one], MigrationOmission.EveryOne(Run)));

        Assert.Contains("written down", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARunThatWroteDownARefusalItNeverCountedIsRefused()
    {
        MigrationDetail one = MigrationDetail.Of(
            Run,
            Refused(MigrationPopulation.Recordings, "7", MigrationRefusal.ReallyEmpty));

        Assert.Throws<ArgumentException>(
            () => MigrationReport.Of(Ran(), Empty(), [one], MigrationOmission.EveryOne(Run)));
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
            () => MigrationReport.Of(Ran(), counted, [one, again], MigrationOmission.EveryOne(Run)));
    }

    [Fact]
    public void AReportCarriesTheRowsOfItsOwnRunAndNoOthers()
    {
        IReadOnlyList<MigrationTally> elsewhere =
            [.. MigrationPopulations.Counted.Select(
                population => MigrationTally.Rehydrate(Another, population, 0, 0, 0, 0))];

        Assert.Throws<ArgumentException>(
            () => MigrationReport.Of(Ran(), elsewhere, [], MigrationOmission.EveryOne(Run)));

        Assert.Throws<ArgumentException>(
            () => MigrationReport.Of(Ran(), Empty(), [], MigrationOmission.EveryOne(Another)));
    }

    [Fact]
    public void AReportOfARunThatFoundNothingIsStillAReport()
    {
        MigrationReport told = MigrationReport.Of(Ran(), Empty(), [], MigrationOmission.EveryOne(Run));

        Assert.Equal(5, told.Tallies.Count);
        Assert.Empty(told.Details);
        Assert.Equal(4, told.Omissions.Count);
    }
}
