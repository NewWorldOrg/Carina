using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Reservations;

public sealed class ReservationOutcomeScramblingTests
{
    private static readonly DateTime Later = ReservationFactory.Now.AddDays(2);

    [Fact]
    public void ARecordingThatCameOutWholeButScrambledIsAFailureOfTheRecording()
    {
        ReservationOutcome line = Scrambled(RecordingOutcome.Complete);

        Assert.Equal(ReservationOutcomeKind.RecordingFailure, line.Kind);
        Assert.Equal(RecordingOutcome.Complete, line.RecordingOutcome);
        Assert.Equal([RecordingFault.ScramblingUnresolved], line.Faults);
        Assert.True(line.LeftScrambled);
        Assert.Null(line.DescrambledAt);
    }

    [Fact]
    public void ARecordingThatCameOutWholeIsAFailureOnlyWhenItWasLeftScrambled()
    {
        Assert.Throws<ArgumentException>(
            () => Record(ReservationOutcomeKind.RecordingFailure, RecordingOutcome.Complete, []));
        Assert.Throws<ArgumentException>(
            () => Record(
                ReservationOutcomeKind.RecordingFailure,
                RecordingOutcome.Complete,
                [RecordingFault.HeavierThanTheStream]));

        ReservationOutcome line = Record(
            ReservationOutcomeKind.RecordingFailure,
            RecordingOutcome.Complete,
            [RecordingFault.HeavierThanTheStream, RecordingFault.ScramblingUnresolved]);

        Assert.True(line.LeftScrambled);
    }

    [Fact]
    public void ALineThatNamesNoScramblingIsNotLeftScrambled()
    {
        ReservationOutcome line = Record(
            ReservationOutcomeKind.RecordingFailure,
            RecordingOutcome.Truncated,
            [RecordingFault.ShortOfTheWindow]);

        Assert.False(line.LeftScrambled);
    }

    [Theory]
    [InlineData(RecordingOutcome.Complete)]
    [InlineData(RecordingOutcome.Truncated)]
    [InlineData(RecordingOutcome.Failed)]
    public void DescramblingLiftsTheLineAndKeepsWhatItSaid(RecordingOutcome outcome)
    {
        ReservationOutcome line = Scrambled(outcome);

        line.Descrambled(Later);

        Assert.False(line.LeftScrambled);
        Assert.Equal(Later, line.DescrambledAt);
        Assert.Equal(ReservationOutcomeKind.RecordingFailure, line.Kind);
        Assert.Equal(outcome, line.RecordingOutcome);
        Assert.Equal([RecordingFault.ScramblingUnresolved], line.Faults);
    }

    [Fact]
    public void ALineThatWasNeverLeftScrambledIsNotDescrambled()
    {
        ReservationOutcome line = Record(ReservationOutcomeKind.Missed, null, []);

        Assert.Throws<InvalidOperationException>(() => line.Descrambled(Later));
        Assert.Null(line.DescrambledAt);
    }

    [Fact]
    public void ALineIsDescrambledOnce()
    {
        ReservationOutcome line = Scrambled(RecordingOutcome.Complete);
        line.Descrambled(Later);

        Assert.Throws<InvalidOperationException>(() => line.Descrambled(Later.AddDays(1)));
        Assert.Equal(Later, line.DescrambledAt);
    }

    [Fact]
    public void ALineIsNotDescrambledBeforeItWasWritten()
    {
        ReservationOutcome line = Scrambled(RecordingOutcome.Complete);

        Assert.Throws<ArgumentException>(() => line.Descrambled(ReservationFactory.Now.AddSeconds(-1)));
        Assert.Throws<ArgumentException>(
            () => line.Descrambled(DateTime.SpecifyKind(Later, DateTimeKind.Local)));
        Assert.True(line.LeftScrambled);
    }

    [Fact]
    public void ARowReadBackKeepsWhenItWasDescrambled()
    {
        ReservationOutcome again = Again(Scrambled(RecordingOutcome.Complete), Later);

        Assert.Equal(Later, again.DescrambledAt);
        Assert.False(again.LeftScrambled);
    }

    [Fact]
    public void ARowCannotSayItWasDescrambledUnlessItNamedTheScramblingBeforeThat()
    {
        ReservationOutcome truncated = Record(
            ReservationOutcomeKind.RecordingFailure,
            RecordingOutcome.Truncated,
            [RecordingFault.ShortOfTheWindow]);

        Assert.Throws<ArgumentException>(() => Again(truncated, Later));
        Assert.Throws<ArgumentException>(
            () => Again(Scrambled(RecordingOutcome.Complete), ReservationFactory.Now.AddSeconds(-1)));
    }

    private static ReservationOutcome Scrambled(RecordingOutcome outcome)
        => Record(ReservationOutcomeKind.RecordingFailure, outcome, [RecordingFault.ScramblingUnresolved]);

    private static ReservationOutcome Record(
        ReservationOutcomeKind kind,
        RecordingOutcome? recordingOutcome,
        IReadOnlyList<RecordingFault> faults)
        => ReservationOutcome.Record(
            ReservationOutcomeId.New(),
            ReservationFactory.Planned(),
            kind,
            null,
            recordingOutcome,
            faults,
            [],
            ReservationFactory.Now);

    private static ReservationOutcome Again(ReservationOutcome held, DateTime? descrambledAt)
        => ReservationOutcome.Rehydrate(
            held.Id,
            held.ReservationId,
            new(held.NetworkId, held.ServiceId, held.EventId, held.ProgrammeStartsAt),
            held.SnapshotName,
            held.EffectiveStartAt,
            held.EffectiveEndAt,
            held.Priority,
            held.RuleId,
            held.Kind,
            held.TuneFailure,
            held.RecordingOutcome,
            held.Faults,
            held.RecordedInstead,
            held.OccurredAt,
            held.RetryResult,
            held.GaveUpBecause,
            descrambledAt);
}
