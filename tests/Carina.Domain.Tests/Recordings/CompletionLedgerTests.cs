using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class CompletionLedgerTests
{
    private static readonly DateTime Now = RecordingFactory.Now;

    public static TheoryData<long?, int, bool> Endings => new()
    {
        { CompletionFactory.TypicalBytes, 1000, true },
        { 0, 1000, true },
        { null, 1000, true },
        { CompletionFactory.TypicalBytes, 1000, false },
        { 2_400_000_000, 960, true },
        { 1_700_000_000, 1000, true },
        { 3_300_000_001, 1000, true },
        { 2_250_000_000, 900, true },
    };

    [Theory]
    [MemberData(nameof(Endings))]
    public void AVerdictIsWrittenToTheLedgerWithoutTranslation(long? bytes, int writtenSeconds, bool asked)
    {
        RecordingEvidence evidence = CompletionFactory.Evidence(
            bytes,
            TimeSpan.FromSeconds(writtenSeconds),
            asked);
        RecordingVerdict verdict = CompletionFactory.Judge(evidence);

        Recording recording = Settle(evidence, verdict);

        Assert.Equal(verdict.Outcome, recording.Outcome);
        Assert.Equal(verdict.Faults, recording.OutcomeDetail.Select(detail => detail.Fault).ToArray());
    }

    [Fact]
    public void ARecordingThisSideAskedToStopIsWrittenDownAsComplete()
    {
        RecordingEvidence evidence = CompletionFactory.Evidence();
        RecordingVerdict verdict = CompletionFactory.Judge(evidence);

        Recording recording = Settle(evidence, verdict);

        Assert.Equal(RecordingOutcome.Complete, recording.Outcome);
        Assert.Empty(recording.OutcomeDetail);
    }

    [Fact]
    public void ARecordingNobodyAskedToStopIsWrittenDownWithTheReasonItIsNotComplete()
    {
        RecordingEvidence evidence = CompletionFactory.Evidence(asked: false);
        RecordingVerdict verdict = CompletionFactory.Judge(evidence);

        Recording recording = Settle(evidence, verdict);

        Assert.Equal(RecordingOutcome.Truncated, recording.Outcome);
        Assert.Equal([RecordingFault.StoppedUnasked], recording.OutcomeDetail.Select(detail => detail.Fault).ToArray());
    }

    [Fact]
    public void AnEmptyFileAndAnUnweighedOneAreToldApartInTheLedger()
    {
        RecordingEvidence empty = CompletionFactory.Evidence(bytes: 0);
        RecordingEvidence unweighed = CompletionFactory.Evidence(bytes: null);

        Recording emptied = Settle(empty, CompletionFactory.Judge(empty));
        Recording unknown = Settle(unweighed, CompletionFactory.Judge(unweighed));

        Assert.Equal(RecordingOutcome.Failed, emptied.Outcome);
        Assert.Equal(RecordingOutcome.Failed, unknown.Outcome);
        Assert.Equal([RecordingFault.NothingLanded], emptied.OutcomeDetail.Select(detail => detail.Fault).ToArray());
        Assert.Equal([RecordingFault.SizeUnobserved], unknown.OutcomeDetail.Select(detail => detail.Fault).ToArray());
    }

    [Fact]
    public void EveryFaultTheVerdictCanNameIsOneTheLedgerAccepts()
    {
        foreach (RecordingFault fault in Enum.GetValues<RecordingFault>()
            .Except(CompletionVerdictTests.FaultsOnlyTheSupervisorCanName))
        {
            Recording recording = RecordingFactory.Started();
            recording.Note(new OutcomeDetail(fault, null, string.Empty, Now));

            Assert.Equal([fault], recording.OutcomeDetail.Select(detail => detail.Fault).ToArray());
        }
    }

    private static Recording Settle(RecordingEvidence evidence, RecordingVerdict verdict)
    {
        Recording recording = RecordingFactory.Started();

        if (evidence.AbortedAt is not null)
        {
            recording.Abort(Now);
        }

        foreach (OutcomeDetail detail in verdict.Detail(Now))
        {
            recording.Note(detail);
        }

        recording.Settle(verdict.Outcome, evidence.FileSizeBytes ?? 0, Now);

        return recording;
    }
}
