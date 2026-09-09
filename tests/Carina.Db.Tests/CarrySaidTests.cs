using Carina.Domain.Channels;
using Carina.Domain.Migration;

namespace Carina.Db.Tests;

public sealed class CarrySaidTests
{
    private static readonly DateTime Began = new(2026, 9, 8, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Ended = new(2026, 9, 8, 3, 1, 0, DateTimeKind.Utc);

    private static readonly MigrationRunId Run = new(new Guid("00000001-0000-0000-0000-000000000001"));

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
    public void WhatWasCarriedAndArrivedDiminishedIsSaidTooSoItDoesNotReadAsAFeatureThatWentMissing()
    {
        string said = CarrySaid.Of(Report(MigrationPass.Rehearsal));

        Assert.Contains(
            "Carried and diminished, DuplicateAvoidance: 1 rows.",
            said,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ProgrammeGuide", said, StringComparison.Ordinal);
        Assert.DoesNotContain("QualityTimeSeries", said, StringComparison.Ordinal);
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
        Assert.DoesNotContain("what the rescan calls it", said, StringComparison.Ordinal);
        Assert.DoesNotContain("what somebody receives it on", said, StringComparison.Ordinal);
    }

    [Fact]
    public void WhatBecameOfTheChannelDefinitionsIsSaidAsCountsAndNothingIsSettledByTheRun()
    {
        string said = CarrySaid.Of(Report(MigrationPass.Rehearsal));

        Assert.Contains(
            "Channel definitions, NameProposed: 1. Nothing was settled by this run.",
            said,
            StringComparison.Ordinal);
    }

    [Fact]
    public void HowManyRulesCrossedOverAndHowManyOfThemWereOnAtTheSourceIsSaid()
    {
        string said = CarrySaid.Of(Report(MigrationPass.Rehearsal));

        Assert.Contains(
            "Rules converted, every one of them turned off: 1, of which 1 were on at the source.",
            said,
            StringComparison.Ordinal);
    }

    [Fact]
    public void HowManyRowsTheSubstitutionTouchedIsSaidWithoutSayingWhatTheySaid()
    {
        string said = CarrySaid.Of(Report(MigrationPass.Rehearsal));

        Assert.Contains(
            "Carried and diminished, EnclosedCharacters: 2 rows.",
            said,
            StringComparison.Ordinal);
    }

    private static MigrationReport Report(MigrationPass pass)
        => MigrationCensus.Taken(
            Run,
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
            Aftermath(),
            Began,
            Ended);

    private static MigrationAftermath Aftermath()
        => new(
            [
                MigrationChannelProposal.Rehydrate(
                    Run,
                    new NetworkId(32736),
                    new ServiceId(1024),
                    MigrationChannelStanding.NameProposed,
                    "what somebody receives",
                    "what somebody receives it on",
                    "what the rescan calls it"),
            ],
            [MigrationRuleProposal.Rehydrate(Run, 3, null, true)],
            1,
            2);
}
