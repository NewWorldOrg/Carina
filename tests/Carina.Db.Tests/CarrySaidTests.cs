using Carina.Domain.Migration;

namespace Carina.Db.Tests;

public sealed class CarrySaidTests
{
    private static readonly DateTime Began = new(2026, 9, 8, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Ended = new(2026, 9, 8, 3, 1, 0, DateTimeKind.Utc);

    [Fact]
    public void EveryPopulationIsCountedWhetherAnythingWasLeftBehindOrNot()
    {
        string said = CarrySaid.Of(Report(MigrationPass.Rehearsal));

        Assert.Contains("A rehearsal", said, StringComparison.Ordinal);
        Assert.Contains(
            "Recordings: 2 offered, 1 carried, 1 not carried, 0 unclassified.",
            said,
            StringComparison.Ordinal);
        Assert.Contains(
            "ChannelDefinitions: 1 offered, 1 carried, 0 not carried, 0 unclassified.",
            said,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WhatWasLeftBehindIsCountedUnderTheReasonItWasLeftBehindFor()
    {
        string said = CarrySaid.Of(Report(MigrationPass.Rehearsal));

        Assert.Contains("Not carried, Recordings ReallyEmpty: 1.", said, StringComparison.Ordinal);
        Assert.Contains("Not carried, RecordingFiles Orphan: 1.", said, StringComparison.Ordinal);
        Assert.Contains("Not carried, Reservations OutOfScope: 1.", said, StringComparison.Ordinal);
    }

    [Fact]
    public void WhatWasDeliberatelyNotDoneIsSaidTooSoItDoesNotReadAsAFeatureThatWentMissing()
    {
        string said = CarrySaid.Of(Report(MigrationPass.Rehearsal));

        Assert.Contains(
            "Nothing was done about ProgrammeGuide: NotMigratedByDesign.",
            said,
            StringComparison.Ordinal);
        Assert.Contains(
            "Nothing was done about QualityTimeSeries: NothingToCarry.",
            said,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ARunForRealSaysThatIsWhatItWas()
        => Assert.Contains("A run for real", CarrySaid.Of(Report(MigrationPass.ForReal)), StringComparison.Ordinal);

    [Fact]
    public void WhatIsSaidOnTheWayOutIsCountsAndReasonsAndNeverWhatAnybodyRecorded()
    {
        string said = CarrySaid.Of(Report(MigrationPass.Rehearsal));

        Assert.DoesNotContain("what somebody watches", said, StringComparison.Ordinal);
        Assert.DoesNotContain("what somebody asked for", said, StringComparison.Ordinal);
        Assert.DoesNotContain("what somebody receives", said, StringComparison.Ordinal);
        Assert.DoesNotContain("bash.sh", said, StringComparison.Ordinal);
    }

    private static MigrationReport Report(MigrationPass pass)
        => MigrationCensus.Taken(
            MigrationRunId.New(),
            new MigrationSourceName("the recording system being replaced"),
            pass,
            MigrationRoll.Of(
                new Dictionary<MigrationPopulation, int>
                {
                    [MigrationPopulation.Recordings] = 2,
                    [MigrationPopulation.RecordingFiles] = 1,
                    [MigrationPopulation.Rules] = 1,
                    [MigrationPopulation.Reservations] = 1,
                    [MigrationPopulation.ChannelDefinitions] = 1,
                },
                [
                    MigrationVerdict.Carry(
                        MigrationPopulation.Recordings,
                        "1",
                        "what somebody watches",
                        null,
                        4096),
                    MigrationVerdict.Refuse(
                        MigrationPopulation.Recordings,
                        "2",
                        MigrationRefusal.ReallyEmpty,
                        "what somebody watches",
                        4096,
                        0),
                    MigrationVerdict.Refuse(
                        MigrationPopulation.RecordingFiles,
                        "bash.sh",
                        MigrationRefusal.Orphan,
                        "bash.sh",
                        null,
                        539),
                    MigrationVerdict.Carry(
                        MigrationPopulation.Rules,
                        "1",
                        "what somebody asked for",
                        null,
                        null),
                    MigrationVerdict.Refuse(
                        MigrationPopulation.Reservations,
                        "1",
                        MigrationRefusal.OutOfScope,
                        "what somebody asked for",
                        null,
                        null),
                    MigrationVerdict.Carry(
                        MigrationPopulation.ChannelDefinitions,
                        "1",
                        "what somebody receives",
                        null,
                        null),
                ]),
            Began,
            Ended);
}
