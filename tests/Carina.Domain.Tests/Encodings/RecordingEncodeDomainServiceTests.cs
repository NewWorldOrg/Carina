using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Encodings;

public sealed class RecordingEncodeDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime At = new(2026, 11, 12, 3, 0, 0, DateTimeKind.Utc);

    private static readonly OutputRoot Shelf = new("shelf");

    private readonly HeldEncodeJobs jobs = new();

    private readonly RecordingId recording = RecordingId.New();

    private RecordingEncodeDomainService Service => new(jobs);

    [Fact(DisplayName = "a recording with a job waiting or running has an encode under way, and one whose jobs have all ended has none")]
    public async Task AnEncodeIsUnderWayWhileAJobWaitsOrRuns()
    {
        EncodeJob waiting = Queued(recording, EncodeProfileId.New());
        jobs.Jobs.Add(Completed(recording, EncodeProfileId.New()));

        Assert.False(await Service.AnyUnderWayAsync(recording, Cancel));

        jobs.Jobs.Add(waiting);

        Assert.True(await Service.AnyUnderWayAsync(recording, Cancel));
        Assert.False(await Service.AnyUnderWayAsync(RecordingId.New(), Cancel));
    }

    [Fact(DisplayName = "BR-ED2-017: erasing a recording sweeps every job that ended and removes each artefact that stands once per name, leaving the replaced one to its sweep")]
    public async Task ErasingARecordingSweepsTheEndedJobsAndRemovesWhatStands()
    {
        EncodeProfileId profile = EncodeProfileId.New();
        EncodeJob replaced = Completed(recording, profile);
        EncodeJob calledOff = Queued(recording, profile);
        calledOff.Cancel(At.AddMinutes(-30));
        EncodeJob latest = Completed(recording, EncodeProfileId.New());
        replaced.Replaced(latest, At);
        EncodeJob waiting = Queued(recording, profile);
        jobs.Jobs.AddRange([replaced, calledOff, latest, waiting, Completed(RecordingId.New(), profile)]);

        RecordingEncodeErasure erasure = await Service.WhatToEraseAsync(recording, Cancel);

        Assert.Equal([replaced.Id, calledOff.Id, latest.Id], erasure.Ended.Select(job => job.Id));
        Assert.Equal([latest.Id], erasure.Standing.Select(job => job.Id));
    }

    [Fact(DisplayName = "erasing a recording removes a file two standing jobs name under the same output root once")]
    public async Task AFileTwoStandingJobsNameIsRemovedOnce()
    {
        EncodeProfileId profile = EncodeProfileId.New();
        EncodeJob first = Completed(recording, profile);
        EncodeJob second = Completed(recording, profile);
        jobs.Jobs.AddRange([first, second]);

        RecordingEncodeErasure erasure = await Service.WhatToEraseAsync(recording, Cancel);

        Assert.Equal([first.Id], erasure.Standing.Select(job => job.Id));
    }

    [Fact(DisplayName = "BR-ED2-012: an artefact whose ending was not kept is still another completed job's when that job stands at the same name")]
    public async Task AnotherStandingJobAtTheSameNameKeepsTheFile()
    {
        EncodeProfileId profile = EncodeProfileId.New();
        EncodeJob earlier = Completed(recording, profile);
        EncodeJob ended = Completed(recording, profile);
        jobs.Jobs.AddRange([earlier, ended]);

        Assert.True(await Service.AnotherStandsAtTheNameOfAsync(ended, Cancel));
    }

    [Fact(DisplayName = "BR-ED2-012: an artefact whose ending was not kept is no other job's when none stands at its name")]
    public async Task NoOtherStandingJobLeavesTheFileToBeRemoved()
    {
        EncodeProfileId profile = EncodeProfileId.New();
        EncodeJob ended = Completed(recording, profile);
        EncodeJob elsewhere = Completed(recording, EncodeProfileId.New());
        EncodeJob failed = Queued(recording, profile);
        failed.Start(At.AddHours(-1));
        failed.Name(EncodeFileName.Artefact(recording, profile));
        failed.Fail(EncodeFailure.TimedOut, "late", At);
        jobs.Jobs.AddRange([ended, elsewhere, failed]);

        Assert.False(await Service.AnotherStandsAtTheNameOfAsync(ended, Cancel));
    }

    private static EncodeJob Queued(RecordingId id, EncodeProfileId profile)
        => EncodeJob.Queue(EncodeJobId.New(), id, profile, EncodeDestinationId.New(), Shelf, At.AddHours(-2));

    private static EncodeJob Completed(RecordingId id, EncodeProfileId profile)
    {
        EncodeJob job = Queued(id, profile);
        job.Start(At.AddHours(-2));
        job.Name(EncodeFileName.Artefact(id, profile));
        job.Complete(At.AddHours(-1));

        return job;
    }
}
