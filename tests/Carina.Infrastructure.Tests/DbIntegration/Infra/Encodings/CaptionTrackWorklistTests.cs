using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Tests.Fixtures.Encodings;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Infra.Encodings;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class CaptionTrackWorklistTests(RepositoryDatabase database) : IAsyncLifetime, IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime MadeAt = EncodeHarness.Queued.AddHours(2);

    private readonly EncodeHarness harness = new();

    private readonly ServiceProvider container = database.Infrastructure();

    public Task InitializeAsync() => ClearAsync();

    public async Task DisposeAsync()
    {
        await ClearAsync();
        await container.DisposeAsync();
    }

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

        await using AsyncServiceScope context = container.CreateAsyncScope();
        CaptionTrackWorklist worklist = CaptionTracks(context);

        Assert.Empty(await worklist.AwaitingAsync(64, Cancel));
        Assert.False(await worklist.StandsWithNothingInHandAsync(standing, Cancel));
    }

    [Fact]
    public async Task BrEd2019AnArtefactThatStandsWithNothingInHandSaysSoAndAReplacedOneDoesNot()
    {
        EncodeJob standing = await StandingAsync(CaptionState.Ready);

        await using AsyncServiceScope context = container.CreateAsyncScope();
        CaptionTrackWorklist worklist = CaptionTracks(context);

        Assert.True(await worklist.StandsWithNothingInHandAsync(standing, Cancel));

        await Context(context).Set<EncodeJob>()
            .Where(job => job.Id == standing.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(job => job.ReplacedAt, EncodeHarness.Queued.AddHours(3)), Cancel);

        Assert.False(await worklist.StandsWithNothingInHandAsync(standing, Cancel));
        Assert.Empty(await worklist.AwaitingAsync(64, Cancel));
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
        await using AsyncServiceScope context = container.CreateAsyncScope();

        return await CaptionTracks(context).AwaitingAsync(atMost, Cancel);
    }

    private async Task<EncodeJob> StandingAsync(CaptionState captions, Action<EncodeJob>? tracked = null, EncodeTimeline? timeline = null)
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
            timeline,
            null);

        tracked?.Invoke(job);

        await using AsyncServiceScope writing = container.CreateAsyncScope();
        await Recordings(writing).AddAsync(recording, Cancel);
        await Profiles(writing).AddAsync(profile, Cancel);
        await Destinations(writing).AddAsync(destination, Cancel);
        await Jobs(writing).AddAsync(job, Cancel);

        return job;
    }

    private async Task AddAsync(EncodeJob job)
    {
        await using AsyncServiceScope writing = container.CreateAsyncScope();
        EncodeJob standing = await Context(writing).Set<EncodeJob>().AsNoTracking().FirstAsync(held => held.RecordingId == job.RecordingId, Cancel);
        await Jobs(writing).AddAsync(
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
        await using AsyncServiceScope clearing = container.CreateAsyncScope();
        await Context(clearing).Set<EncodeChapter>().ExecuteDeleteAsync(Cancel);
        await Context(clearing).Set<EncodeScratchFile>().ExecuteDeleteAsync(Cancel);
        await Context(clearing).Set<EncodeJob>().ExecuteDeleteAsync(Cancel);
    }

    private static CaptionTrackWorklist CaptionTracks(AsyncServiceScope scope)
        => ActivatorUtilities.CreateInstance<CaptionTrackWorklist>(scope.ServiceProvider);

    private static IEncodeDestinationRepository Destinations(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IEncodeDestinationRepository>();

    private static IEncodeJobRepository Jobs(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IEncodeJobRepository>();

    private static IEncodeProfileRepository Profiles(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IEncodeProfileRepository>();

    private static IRecordingRepository Recordings(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IRecordingRepository>();

    private static CarinaDbContext Context(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<CarinaDbContext>();
}
