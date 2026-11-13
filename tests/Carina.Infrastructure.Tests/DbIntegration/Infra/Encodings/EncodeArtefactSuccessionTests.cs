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
public sealed class EncodeArtefactSuccessionTests(RepositoryDatabase database) : IAsyncLifetime
{
    private static readonly DateTime MadeFirst = new(2026, 9, 5, 3, 10, 0, DateTimeKind.Utc);

    private static readonly DateTime MadeAgain = new(2026, 9, 5, 3, 40, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ServiceProvider container = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await container.DisposeAsync();

    [Fact(DisplayName = "settling through the ledger marks the earlier job replaced and writes down the removal it owes in one write, the file goes, and it reads back so")]
    public async Task SettlingThroughTheLedgerMarksTheEarlierJobReplacedAndItsFileGoes()
    {
        await ClearAsync();
        using EncodeHarness harness = new();
        RecordingId recording = RecordingId.New();
        EncodeJob earlier = Completed(recording, MadeFirst);
        EncodeJob newer = Completed(recording, MadeAgain);
        await AddAsync(earlier, newer);
        File.WriteAllText(harness.ArtefactPathOf(earlier), "the first picture");
        File.WriteAllText(harness.ArtefactPathOf(newer), "the second picture");

        await using ServiceProvider standingIn = database.Infrastructure(harness.StandIn);
        await using (AsyncServiceScope settling = standingIn.CreateAsyncScope())
        {
            EncodeSuccessionReport report = await settling.ServiceProvider.GetRequiredService<EncodeArtefactSuccession>().SettleAsync(recording, Cancel);

            Assert.Equal(1, report.Replaced);
            Assert.Equal(1, report.FilesRemoved);
        }

        Assert.False(File.Exists(harness.ArtefactPathOf(earlier)));
        Assert.Equal("the second picture", File.ReadAllText(harness.ArtefactPathOf(newer)));

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IEncodeJobRepository jobs = Jobs(reading);
        EncodeJob readEarlier = (await jobs.FindAsync(earlier.Id, Cancel))!;
        EncodeJob readNewer = (await jobs.FindAsync(newer.Id, Cancel))!;
        Assert.Equal(harness.Clock.GetUtcNow().UtcDateTime, readEarlier.ReplacedAt);
        Assert.Equal(earlier.ArtefactName, readEarlier.ArtefactName);
        Assert.True(readNewer.StandsAsTheArtefact);

        EncodeScratchFile owed = await Context(reading).Set<EncodeScratchFile>().SingleAsync(Cancel);
        Assert.Equal(EncodeScratchKind.ReplacedArtefact, owed.Kind);
        Assert.Equal(earlier.Id, owed.JobId);
        Assert.Equal(EncodeScratchFate.Removed, owed.Fate);
    }

    private static EncodeJob Running(RecordingId recording)
    {
        EncodeJob job = EncodeJob.Queue(
            EncodeJobId.New(),
            recording,
            EncodeProfileId.New(),
            EncodeDestinationId.New(),
            EncodeHarness.Encodes,
            EncodeHarness.Queued);
        job.Start(EncodeHarness.Started);

        return job;
    }

    private static EncodeJob Completed(RecordingId recording, DateTime ended)
    {
        EncodeJob job = Running(recording);
        job.Name(EncodeFileName.Artefact(recording, job.ProfileId));
        job.Complete(ended);

        return job;
    }

    private async Task AddAsync(params EncodeJob[] jobs)
    {
        await using AsyncServiceScope writing = container.CreateAsyncScope();
        IEncodeProfileRepository profiles = Profiles(writing);
        IEncodeDestinationRepository destinations = Destinations(writing);
        IEncodeJobRepository repository = Jobs(writing);

        foreach (EncodeJob job in jobs)
        {
            await profiles.AddAsync(
                EncodeProfile.Define(
                    job.ProfileId,
                    new EncodeLabel("Viewing"),
                    EncodeCodec.H264,
                    EncodeResolution.AsSource,
                    Deinterlace.EveryFrame,
                    new ConstantRateFactor(22),
                    new ConstantQuantiser(24),
                    EncodeHarness.Queued),
                Cancel);
            await destinations.AddAsync(
                EncodeDestination.Define(
                    job.DestinationId,
                    new EncodeLabel("Shelf"),
                    job.OutputRoot,
                    job.ProfileId,
                    EncodeHarness.Queued),
                Cancel);
            await repository.AddAsync(job, Cancel);
        }
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

    private static CarinaDbContext Context(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<CarinaDbContext>();
}
