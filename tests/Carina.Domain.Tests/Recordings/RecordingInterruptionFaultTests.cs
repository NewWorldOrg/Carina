using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingInterruptionFaultTests
{
    private static readonly DateTime Now = RecordingFactory.Now;

    public static TheoryData<RecordingFault> Breaking => Set(RecordingFaults.ThatCanInterrupt);

    public static TheoryData<RecordingFault> NotBreaking
        => Set(Enum.GetValues<RecordingFault>().Except(RecordingFaults.ThatCanInterrupt));

    [Theory]
    [MemberData(nameof(Breaking))]
    public void ARecordingBreaksOnAnythingThatHappensWhileItRuns(RecordingFault fault)
    {
        Recording recording = RecordingFactory.Started();

        recording.Interrupt(fault, Now);

        Assert.Equal([fault], recording.Interruptions.Select(interruption => interruption.Fault).ToArray());
    }

    [Theory]
    [MemberData(nameof(NotBreaking))]
    public void ARecordingDoesNotBreakOnAReasonThatIsNotSomethingHappeningToIt(RecordingFault fault)
    {
        Recording recording = RecordingFactory.Started();

        ArgumentOutOfRangeException refusal =
            Assert.Throws<ArgumentOutOfRangeException>(() => recording.Interrupt(fault, Now));

        Assert.Equal("fault", refusal.ParamName);
    }

    [Fact]
    public void TheFaultsThatBreakARecordingAreTheOnesTheCrossCheckNeverNames()
        => Assert.Empty(RecordingFaults.ThatCanInterrupt.Intersect(CompletionFactory.FaultsTheCrossCheckNames));

    [Fact]
    public void EveryFaultTheLedgerHoldsBreaksARecordingOrConcludesOneOrExplainsItsWindow()
        => Assert.Equal(
            Enum.GetValues<RecordingFault>().Order().ToArray(),
            RecordingFaults.ThatCanInterrupt
                .Concat(CompletionFactory.FaultsTheCrossCheckNames)
                .Concat(RecordingFaults.ThatExplainTheWindow)
                .Order()
                .ToArray());

    [Fact]
    public void NoFaultIsInTwoOfThoseThreeAtOnce()
    {
        Assert.Empty(RecordingFaults.ThatCanInterrupt.Intersect(RecordingFaults.ThatExplainTheWindow));
        Assert.Empty(CompletionFactory.FaultsTheCrossCheckNames.Intersect(RecordingFaults.ThatExplainTheWindow));
    }

    [Fact]
    public void AFaultTheLedgerDoesNotHoldStillBreaksNothing()
    {
        Recording recording = RecordingFactory.Started();

        Assert.Throws<ArgumentOutOfRangeException>(() => recording.Interrupt((RecordingFault)99, Now));
    }

    [Fact]
    public void AReasonMayStillNameSomethingOnlyTheEndingKnows()
    {
        Recording recording = RecordingFactory.Started();

        recording.Note(new OutcomeDetail(RecordingFault.HeavierThanTheStream, null, string.Empty, Now));

        Assert.Equal(
            [RecordingFault.HeavierThanTheStream],
            recording.OutcomeDetail.Select(detail => detail.Fault).ToArray());
    }

    private static TheoryData<RecordingFault> Set(IEnumerable<RecordingFault> faults)
    {
        var named = new TheoryData<RecordingFault>();
        foreach (RecordingFault fault in faults)
        {
            named.Add(fault);
        }

        return named;
    }
}
