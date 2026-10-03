using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests.Encodings;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class CaptionTrackWorklistTests(RepositoryDatabase database) : IAsyncLifetime, IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime MadeAt = EncodeHarness.Queued.AddHours(2);

    private readonly EncodeHarness harness = new();

    public Task InitializeAsync() => ClearAsync();

    public Task DisposeAsync() => ClearAsync();

    public void Dispose() => harness.Dispose();

    [Fact]
    public async Task BrEd2019AnArtefactOfARecordingWhoseCaptionsAreReadyAwaitsATrackUntilOneIsTriedFromThatRecord()
    {
        EncodeJob untried = await StandingAsync(CaptionState.Ready);
        EncodeJob added = await StandingAsync(CaptionState.Ready, job => job.Tracked(EncodeCaptionTrack.Added, MadeAt));
        EncodeJob addedFromAnOlderRecord = await StandingAsync(CaptionState.Ready, job => job.Tracked(EncodeCaptionTrack.Added, MadeAt.AddHours(-1)));
        EncodeJob withheld = await StandingAsync(CaptionState.Ready, job => job.Tracked(EncodeCaptionTrack.Withheld, MadeAt));
        EncodeJob failedTwice = await StandingAsync(CaptionState.Ready, job => Failed(job, 2));
        EncodeJob failedThrice = await StandingAsync(CaptionState.Ready, job => Failed(job, 3));
        EncodeJob absent = await StandingAsync(CaptionState.Absent);

        IReadOnlyList<CaptionTrackSubject> awaiting = await AwaitingAsync(64);

        Assert.Equal(
            new[] { untried.Id, addedFromAnOlderRecord.Id, failedTwice.Id }.Select(id => id.Wire).Order(StringComparer.Ordinal),
            awaiting.Select(subject => subject.Job.Id.Wire).Order(StringComparer.Ordinal));
        Assert.All(awaiting, subject => Assert.Equal(MadeAt, subject.CaptionsMadeAt));
        Assert.DoesNotContain(awaiting, subject => new[] { added.Id, withheld.Id, failedThrice.Id, absent.Id }.Contains(subject.Job.Id));
        Assert.Single(await AwaitingAsync(1));
    }

    [Fact]
    public async Task BrEd2019ARecordingAJobOfWhichIsWaitingOrRunningIsLeftUntilItEnds()
    {
        EncodeJob standing = await StandingAsync(CaptionState.Ready);
        EncodeJob running = harness.Running(standing.RecordingId, standing.ProfileId);
        await AddAsync(running);

        await using CarinaDbContext context = database.Open();
        CaptionTrackWorklist worklist = new(context);

        Assert.Empty(await worklist.AwaitingAsync(64, Cancel));
        Assert.False(await worklist.StandsWithNothingInHandAsync(standing, Cancel));
    }

    [Fact]
    public async Task BrEd2019AnArtefactThatStandsWithNothingInHandSaysSoAndAReplacedOneDoesNot()
    {
        EncodeJob standing = await StandingAsync(CaptionState.Ready);

        await using CarinaDbContext context = database.Open();
        CaptionTrackWorklist worklist = new(context);

        Assert.True(await worklist.StandsWithNothingInHandAsync(standing, Cancel));

        await context.Set<EncodeJob>()
            .Where(job => job.Id == standing.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(job => job.ReplacedAt, EncodeHarness.Queued.AddHours(3)), Cancel);

        Assert.False(await worklist.StandsWithNothingInHandAsync(standing, Cancel));
        Assert.Empty(await worklist.AwaitingAsync(64, Cancel));
    }

    [Fact]
    public async Task BrEd2019WhatBecameOfATrackIsKeptWithTheRecordItCameFromAndTheFailuresInARow()
    {
        EncodeJob standing = await StandingAsync(CaptionState.Ready, job => Failed(job, 2));

        await using CarinaDbContext context = database.Open();
        EncodeJob read = (await new EncodeJobRepository(context).FindAsync(standing.Id, Cancel))!;

        Assert.Equal((EncodeCaptionTrack.Failed, MadeAt, 2), (read.CaptionTrack!.Value, read.CaptionTrackFrom!.Value, read.CaptionTrackAttempts));

        read.Tracked(EncodeCaptionTrack.Added, MadeAt);
        await new EncodeJobRepository(context).SaveAsync(read, Cancel);

        await using CarinaDbContext again = database.Open();
        EncodeJob saved = (await new EncodeJobRepository(again).FindAsync(standing.Id, Cancel))!;

        Assert.Equal((EncodeCaptionTrack.Added, MadeAt, 0), (saved.CaptionTrack!.Value, saved.CaptionTrackFrom!.Value, saved.CaptionTrackAttempts));
    }

    private static void Failed(EncodeJob job, int times)
    {
        for (int time = 0; time < times; time++)
        {
            job.Tracked(EncodeCaptionTrack.Failed, MadeAt);
        }
    }

    private async Task<IReadOnlyList<CaptionTrackSubject>> AwaitingAsync(int atMost)
    {
        await using CarinaDbContext context = database.Open();

        return await new CaptionTrackWorklist(context).AwaitingAsync(atMost, Cancel);
    }

    private async Task<EncodeJob> StandingAsync(CaptionState captions, Action<EncodeJob>? tracked = null)
    {
        Recording recording = harness.Recorded();
        recording.Caption(captions, captions is CaptionState.Ready ? 3 : null, MadeAt);
        EncodeProfile profile = harness.Defined();
        EncodeDestination destination = EncodeDestination.Define(
            EncodeDestinationId.New(),
            new EncodeLabel("Shelf"),
            EncodeHarness.Encodes,
            profile.Id,
            EncodeHarness.Queued);
        EncodeJob job = EncodeJob.Rehydrate(
            EncodeJobId.New(),
            recording.Id,
            profile.Id,
            destination.Id,
            EncodeHarness.Encodes,
            EncodeJobStatus.Completed,
            EncodeJob.FirstAttempt,
            EncodeHarness.Queued,
            EncodeHarness.Started,
            EncodeHarness.Started.AddMinutes(30),
            null,
            EncodeFileName.Artefact(recording.Id, profile.Id),
            null,
            null,
            null,
            null,
            null);

        tracked?.Invoke(job);

        await using CarinaDbContext writing = database.Open();
        await new RecordingRepository(writing).AddAsync(recording, Cancel);
        await new EncodeProfileRepository(writing).AddAsync(profile, Cancel);
        await new EncodeDestinationRepository(writing).AddAsync(destination, Cancel);
        await new EncodeJobRepository(writing).AddAsync(job, Cancel);

        return job;
    }

    private async Task AddAsync(EncodeJob job)
    {
        await using CarinaDbContext writing = database.Open();
        EncodeJob standing = await writing.Set<EncodeJob>().AsNoTracking().FirstAsync(held => held.RecordingId == job.RecordingId, Cancel);
        await new EncodeJobRepository(writing).AddAsync(
            EncodeJob.Rehydrate(
                job.Id,
                job.RecordingId,
                standing.ProfileId,
                standing.DestinationId,
                job.OutputRoot,
                job.Status,
                job.Attempt,
                job.QueuedAt,
                job.StartedAt,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null),
            Cancel);
    }

    private async Task ClearAsync()
    {
        await using CarinaDbContext clearing = database.Open();
        await clearing.Set<EncodeChapter>().ExecuteDeleteAsync(Cancel);
        await clearing.Set<EncodeScratchFile>().ExecuteDeleteAsync(Cancel);
        await clearing.Set<EncodeJob>().ExecuteDeleteAsync(Cancel);
    }
}
