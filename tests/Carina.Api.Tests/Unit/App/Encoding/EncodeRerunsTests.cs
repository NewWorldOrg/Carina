using Carina.Api.Services;
using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Api.Tests.Unit.App.Encoding;

[Trait("Category", "Unit")]
public sealed class EncodeRerunsTests
{
    private static readonly DateTime Noon = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "BR-ED2-008: one request runs at most a hundred jobs again")]
    public void OneRequestRunsAtMostAHundredJobsAgain()
    {
        Assert.Equal(100, EncodeReruns.MostAtOnce);
        Assert.Contains("100", EncodeReruns.Description, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(EncodeJobStatus.Queued, false)]
    [InlineData(EncodeJobStatus.Running, false)]
    [InlineData(EncodeJobStatus.Completed, false)]
    [InlineData(EncodeJobStatus.Failed, true)]
    [InlineData(EncodeJobStatus.Cancelled, false)]
    public void OnlyAFailedJobIsRunAgain(EncodeJobStatus status, bool runAgain)
        => Assert.Equal(runAgain, EncodeReruns.CanRunAgain(Standing(status)));

    [Fact]
    public void EveryStandingAJobCanHaveIsListedAbove()
        => Assert.Equal(
            [EncodeJobStatus.Queued, EncodeJobStatus.Running, EncodeJobStatus.Completed, EncodeJobStatus.Failed, EncodeJobStatus.Cancelled],
            Enum.GetValues<EncodeJobStatus>());

    [Theory]
    [InlineData(EncodingFailure.Refused, EncodeRerunOutcome.Refused)]
    [InlineData(EncodingFailure.DriverUnreachable, EncodeRerunOutcome.Refused)]
    [InlineData(EncodingFailure.DriverRefused, EncodeRerunOutcome.Refused)]
    [InlineData(EncodingFailure.NoSuchProfile, EncodeRerunOutcome.Refused)]
    [InlineData(EncodingFailure.NoSuchDestination, EncodeRerunOutcome.Refused)]
    [InlineData(EncodingFailure.NoSuchRecording, EncodeRerunOutcome.Refused)]
    [InlineData(EncodingFailure.NoSuchJob, EncodeRerunOutcome.NoSuchJob)]
    [InlineData(EncodingFailure.RecordingStillBeingWritten, EncodeRerunOutcome.Refused)]
    [InlineData(EncodingFailure.RecordingFailed, EncodeRerunOutcome.Refused)]
    [InlineData(EncodingFailure.AlreadyInTheQueue, EncodeRerunOutcome.AlreadyInTheQueue)]
    [InlineData(EncodingFailure.AlreadyEncoded, EncodeRerunOutcome.AlreadyEncoded)]
    [InlineData(EncodingFailure.AlreadyOver, EncodeRerunOutcome.Refused)]
    [InlineData(EncodingFailure.MovedMeanwhile, EncodeRerunOutcome.Refused)]
    [InlineData(EncodingFailure.StillInFlight, EncodeRerunOutcome.Refused)]
    [InlineData(EncodingFailure.AlreadyRetired, EncodeRerunOutcome.Refused)]
    [InlineData(EncodingFailure.StillTheDefault, EncodeRerunOutcome.Refused)]
    [InlineData(EncodingFailure.TheLastOne, EncodeRerunOutcome.Refused)]
    public void EveryWayTheQueueCanRefuseReadsAsAnOutcomeOfTheRow(EncodingFailure failure, EncodeRerunOutcome outcome)
        => Assert.Equal(outcome, EncodeReruns.Outcome(failure));

    [Fact]
    public void EveryFailureTheQueueNamesIsListedAbove()
    {
        EncodingFailure[] named =
        [
            EncodingFailure.Refused,
            EncodingFailure.DriverUnreachable,
            EncodingFailure.DriverRefused,
            EncodingFailure.NoSuchProfile,
            EncodingFailure.NoSuchDestination,
            EncodingFailure.NoSuchRecording,
            EncodingFailure.NoSuchJob,
            EncodingFailure.RecordingStillBeingWritten,
            EncodingFailure.RecordingFailed,
            EncodingFailure.AlreadyInTheQueue,
            EncodingFailure.AlreadyEncoded,
            EncodingFailure.AlreadyOver,
            EncodingFailure.MovedMeanwhile,
            EncodingFailure.StillInFlight,
            EncodingFailure.AlreadyRetired,
            EncodingFailure.StillTheDefault,
            EncodingFailure.TheLastOne,
        ];

        Assert.Equal(named.Order(), Enum.GetValues<EncodingFailure>().Order());
    }

    [Fact(DisplayName = "a row that queued nothing says why, and one that queued a job says nothing beside the job")]
    public void ARowSaysWhyNothingWasQueuedAndNothingBesideAJobOtherwise()
    {
        EncodeJobId id = EncodeJobId.New();
        EncodeJob running = Standing(EncodeJobStatus.Running);

        EncodeRerunRow missing = EncodeRerunRow.NoSuchJob(id);
        EncodeRerunRow notFailed = EncodeRerunRow.NotFailed(id, running);
        EncodeRerunRow refused = EncodeRerunRow.NotQueued(id, "the recording is still being written", EncodingFailure.RecordingStillBeingWritten);
        EncodeRerunRow queued = EncodeRerunRow.QueuedAgain(id, new EncodeJobView(Standing(EncodeJobStatus.Queued), null, false, false));
        EncodeRerunRow[] withoutAJob = [missing, notFailed, refused];

        Assert.Equal((id, EncodeRerunOutcome.NoSuchJob), (missing.Id, missing.Outcome));
        Assert.DoesNotContain(id.Wire, missing.Message, StringComparison.Ordinal);
        Assert.Equal(EncodeRerunOutcome.NotFailed, notFailed.Outcome);
        Assert.Contains("Running", notFailed.Message, StringComparison.Ordinal);
        Assert.Equal((EncodeRerunOutcome.Refused, "the recording is still being written"), (refused.Outcome, refused.Message));
        Assert.All(withoutAJob, row => Assert.Null(row.Job));
        Assert.Equal((EncodeRerunOutcome.Queued, string.Empty), (queued.Outcome, queued.Message));
        Assert.NotNull(queued.Job);
    }

    private static EncodeJob Standing(EncodeJobStatus status)
    {
        RecordingId recording = RecordingId.New();
        EncodeProfileId profile = EncodeProfileId.New();
        EncodeJob job = EncodeJob.Queue(EncodeJobId.New(), recording, profile, EncodeDestinationId.New(), new OutputRoot("encodes"), Noon);

        if (status is EncodeJobStatus.Queued)
        {
            return job;
        }

        if (status is EncodeJobStatus.Cancelled)
        {
            job.Cancel(Noon.AddMinutes(1));

            return job;
        }

        job.Start(Noon.AddMinutes(1));

        switch (status)
        {
            case EncodeJobStatus.Completed:
                job.Name(EncodeFileName.Artefact(recording, profile));
                job.Complete(Noon.AddMinutes(2));

                break;
            case EncodeJobStatus.Failed:
                job.Fail(EncodeFailure.TimedOut, "no headway", Noon.AddMinutes(2));

                break;
            default:
                break;
        }

        return job;
    }
}
