using Carina.Domain.Captions;
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
public sealed class ArtefactCaptionTracksTests(RepositoryDatabase database) : IAsyncLifetime, IDisposable
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
    public async Task BrEd2019AnArtefactTriedAgainAfterAFailureIsTriedUnderNewNamesAndWhatCameOfItIsKept()
    {
        TimeSpan sourceStart = TimeSpan.FromSeconds(30499.5);
        EncodeJob standing = await StandingAsync(
            CaptionState.Ready,
            new EncodeTimeline(sourceStart, TimeSpan.FromSeconds(0.5), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30)));
        long shownAt = (long)((sourceStart.TotalSeconds + 2) * 90_000);
        await harness.CaptionShelf.KeepAsync(
            standing.RecordingId,
            new CaptionRecord(1440, 1080, sourceStart, [new CaptionCue(shownAt, null)], [new CaptionLine(shownAt, "字")]),
            Cancel);
        File.WriteAllBytes(harness.ArtefactPathOf(standing), [1, 2, 3, 4]);

        await using ServiceProvider standingIn = database.Infrastructure(harness.StandIn);

        for (int pass = 0; pass < 2; pass++)
        {
            await using AsyncServiceScope context = standingIn.CreateAsyncScope();
            ArtefactCaptioningRound round = await Assert.IsType<ArtefactCaptionTracks>(context.ServiceProvider.GetRequiredService<IArtefactCaptioning>())
                .CaptionAsync(4, _ => Task.FromResult(false), Cancel);

            Assert.Equal((1, 1), (round.Read, round.Failed));
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        EncodeJob read = (await Jobs(reading).FindAsync(standing.Id, Cancel))!;
        List<EncodeScratchFile> scratch = await Context(reading).Set<EncodeScratchFile>().AsNoTracking().Where(file => file.JobId == standing.Id).ToListAsync(Cancel);

        Assert.Equal((EncodeCaptionTrack.Failed, MadeAt, 2), (read.CaptionTrack!.Value, read.CaptionTrackFrom!.Value, read.CaptionTrackAttempts));
        Assert.Equal(4, scratch.Select(file => file.FileName.Value).Distinct().Count());
        Assert.All(scratch, file => Assert.False(file.IsOwedARemoval));
    }

    private async Task<EncodeJob> StandingAsync(CaptionState captions, EncodeTimeline timeline)
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

        await using AsyncServiceScope writing = container.CreateAsyncScope();
        await Recordings(writing).AddAsync(recording, Cancel);
        await Profiles(writing).AddAsync(profile, Cancel);
        await Destinations(writing).AddAsync(destination, Cancel);
        await Jobs(writing).AddAsync(job, Cancel);

        return job;
    }

    private async Task ClearAsync()
    {
        await using AsyncServiceScope clearing = container.CreateAsyncScope();
        await Context(clearing).Set<EncodeChapter>().ExecuteDeleteAsync(Cancel);
        await Context(clearing).Set<EncodeScratchFile>().ExecuteDeleteAsync(Cancel);
        await Context(clearing).Set<EncodeJob>().ExecuteDeleteAsync(Cancel);
    }

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
