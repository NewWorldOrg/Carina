using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Encodings;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class EncodeChapterRepositoryTests(RepositoryDatabase database) : IAsyncLifetime
{
    private static readonly DateTime Defined = new(2026, 9, 5, 2, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Queued = new(2026, 9, 5, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Started = new(2026, 9, 5, 3, 0, 5, DateTimeKind.Utc);

    private static readonly OutputRoot Primary = new("primary");

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ServiceProvider container = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await container.DisposeAsync();

    [Fact(DisplayName = "the chapters a run marked come back in the order they were marked, on the artefact's clock, and only the ones belonging to the job asked for")]
    public async Task TheChaptersARunMarkedComeBackInTheOrderTheyWereMarked()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob job = Job(profile, destination);
        EncodeJob other = Job(profile, destination);
        IReadOnlyList<ChapterSegment> segments =
        [
            new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(30), ChapterKind.Programme),
            new ChapterSegment(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(90), ChapterKind.Break),
            new ChapterSegment(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(130.5), ChapterKind.Programme),
        ];

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            await Jobs(writing).AddAsync(job, Cancel);
            await Jobs(writing).AddAsync(other, Cancel);
        }

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            await Chapters(writing).RecordAsync(job.Id, EncodeChapter.Mark(job.Id, segments), Cancel);
            await Chapters(writing).RecordAsync(
                other.Id,
                EncodeChapter.Mark(other.Id, [new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(5), ChapterKind.Break)]),
                Cancel);
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IReadOnlyList<EncodeChapter> read = await Chapters(reading).ListForJobAsync(job.Id, Cancel);

        Assert.Equal([EncodeChapter.FirstOrdinal, 1, 2], read.Select(chapter => chapter.Ordinal));
        Assert.Equal(segments, read.Select(chapter => chapter.Segment));
        Assert.All(read, chapter => Assert.Equal(job.Id, chapter.JobId));
        Assert.Equal(TimeSpan.FromSeconds(130.5), read[^1].EndsAt);
        Assert.Empty(await Chapters(reading).ListForJobAsync(EncodeJobId.New(), Cancel));
        Assert.Equal(
            [ChapterKind.Break],
            (await Chapters(reading).ListForJobAsync(other.Id, Cancel)).Select(chapter => chapter.Kind));
    }

    [Fact(DisplayName = "a reading of nothing writes nothing, and what a run made of the breaks comes back on the job as it was written")]
    public async Task WhatARunMadeOfTheBreaksComesBackOnTheJobAsItWasWritten()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob judged = Job(profile, destination);
        EncodeJob unjudged = Job(profile, destination);
        judged.Start(Started);
        judged.Judged(new ChapterReading(ChapterDetectorName.Ffmpeg, ChapterVerdict.Discarded, 0.71, Started));

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            await Jobs(writing).AddAsync(judged, Cancel);
            await Jobs(writing).AddAsync(unjudged, Cancel);
            await Chapters(writing).RecordAsync(judged.Id, [], Cancel);
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        EncodeJob? readJudged = await Jobs(reading).FindAsync(judged.Id, Cancel);
        EncodeJob? readUnjudged = await Jobs(reading).FindAsync(unjudged.Id, Cancel);

        Assert.NotNull(readJudged);
        Assert.Equal(ChapterDetectorName.Ffmpeg, readJudged.Chapters!.Detector);
        Assert.Equal(ChapterVerdict.Discarded, readJudged.Chapters.Verdict);
        Assert.Equal(0.71, readJudged.Chapters.BreakShare);
        Assert.Equal(Started, readJudged.Chapters.DecidedAt);
        Assert.Null(readUnjudged!.Chapters);
        Assert.Empty(await Chapters(reading).ListForJobAsync(judged.Id, Cancel));
    }

    [Fact(DisplayName = "a job read a second time holds what the second reading marked and nothing of the first, and a second reading that marked nothing leaves it holding none")]
    public async Task AJobReadASecondTimeHoldsWhatTheSecondReadingMarked()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob job = Job(profile, destination);

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            await Jobs(writing).AddAsync(job, Cancel);
        }

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            await Chapters(writing).RecordAsync(
                job.Id,
                EncodeChapter.Mark(
                    job.Id,
                    [
                        new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(30), ChapterKind.Programme),
                        new ChapterSegment(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(90), ChapterKind.Break),
                    ]),
                Cancel);
        }

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            await Chapters(writing).RecordAsync(
                job.Id,
                EncodeChapter.Mark(job.Id, [new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(45), ChapterKind.Break)]),
                Cancel);
        }

        await using (AsyncServiceScope reading = container.CreateAsyncScope())
        {
            EncodeChapter only = Assert.Single(await Chapters(reading).ListForJobAsync(job.Id, Cancel));

            Assert.Equal(EncodeChapter.FirstOrdinal, only.Ordinal);
            Assert.Equal(TimeSpan.FromSeconds(45), only.EndsAt);
            Assert.Equal(ChapterKind.Break, only.Kind);
        }

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            await Chapters(writing).RecordAsync(job.Id, [], Cancel);
        }

        await using AsyncServiceScope after = container.CreateAsyncScope();
        Assert.Empty(await Chapters(after).ListForJobAsync(job.Id, Cancel));
    }

    [Fact]
    public async Task AJobIsWrittenTheChaptersOfItsOwnReading()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob job = Job(profile, destination);

        await using AsyncServiceScope writing = container.CreateAsyncScope();

        await Assert.ThrowsAsync<ArgumentException>(() => Chapters(writing).RecordAsync(
            job.Id,
            EncodeChapter.Mark(EncodeJobId.New(), [new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(5), ChapterKind.Break)]),
            Cancel));
    }

    private async Task ClearAsync()
    {
        await using AsyncServiceScope clearing = container.CreateAsyncScope();
        await Context(clearing).Set<EncodeChapter>().ExecuteDeleteAsync(Cancel);
        await Context(clearing).Set<EncodeScratchFile>().ExecuteDeleteAsync(Cancel);
        await Context(clearing).Set<EncodeJob>().ExecuteDeleteAsync(Cancel);
    }

    private async Task<(EncodeProfile, EncodeDestination)> DefinedAsync()
    {
        EncodeProfile profile = EncodeProfile.Define(
            EncodeProfileId.New(),
            new EncodeLabel("Viewing"),
            EncodeCodec.H264,
            EncodeResolution.Hd,
            Deinterlace.EveryFrame,
            new ConstantRateFactor(22),
            new ConstantQuantiser(25),
            Defined);
        EncodeDestination destination = EncodeDestination.Define(
            EncodeDestinationId.New(),
            new EncodeLabel("Primary"),
            Primary,
            profile.Id,
            Defined);

        await using AsyncServiceScope writing = container.CreateAsyncScope();
        await Profiles(writing).AddAsync(profile, Cancel);
        await Destinations(writing).AddAsync(destination, Cancel);

        return (profile, destination);
    }

    private static EncodeJob Job(EncodeProfile profile, EncodeDestination destination, RecordingId? recording = null)
        => EncodeJob.Queue(EncodeJobId.New(), recording ?? RecordingId.New(), profile.Id, destination.Id, Primary, Queued);

    private static IEncodeChapterRepository Chapters(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IEncodeChapterRepository>();

    private static IEncodeDestinationRepository Destinations(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IEncodeDestinationRepository>();

    private static IEncodeJobRepository Jobs(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IEncodeJobRepository>();

    private static IEncodeProfileRepository Profiles(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IEncodeProfileRepository>();

    private static CarinaDbContext Context(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<CarinaDbContext>();
}
