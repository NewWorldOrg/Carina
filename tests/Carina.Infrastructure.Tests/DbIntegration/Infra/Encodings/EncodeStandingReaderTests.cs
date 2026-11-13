using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Infra.Encodings;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class EncodeStandingReaderTests(RepositoryDatabase database) : IAsyncLifetime
{
    private const int APageOfTheLibrary = 50;

    private ServiceProvider Watching(RecordedCommands watched)
        => database.Infrastructure(replace => replace.ConfigureDbContext<CarinaDbContext>(options => options.AddInterceptors(watched)));

    private static readonly DateTime Defined = new(2026, 9, 6, 2, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Queued = new(2026, 9, 6, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Started = new(2026, 9, 6, 3, 0, 5, DateTimeKind.Utc);

    private static readonly DateTime Ended = new(2026, 9, 6, 4, 0, 0, DateTimeKind.Utc);

    private static readonly OutputRoot Primary = new("primary");

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ServiceProvider container = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await container.DisposeAsync();

    [Fact(DisplayName = "a whole page of the library is answered by one look at the ledger")]
    public async Task AWholePageOfTheLibraryIsAnsweredByOneLookAtTheLedger()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        RecordingId[] page = [.. Enumerable.Range(0, APageOfTheLibrary).Select(_ => RecordingId.New())];

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            IEncodeJobRepository ledger = Jobs(writing);

            foreach (RecordingId recording in page)
            {
                await ledger.AddAsync(Finished(recording, profile, destination), Cancel);
            }
        }

        var watched = new RecordedCommands();

        await using ServiceProvider watching = Watching(watched);
        await using AsyncServiceScope reading = watching.CreateAsyncScope();
        EncodeStandingBoard board = await Standing(reading).ReadAsync(page, Cancel);

        Assert.Single(watched.Seen);
        Assert.All(page, recording => Assert.Equal(EncodeStanding.Completed, board.For(recording)));
    }

    [Fact(DisplayName = "a recording the ledger holds nothing for stands unencoded rather than missing")]
    public async Task ARecordingTheLedgerHoldsNothingForStandsUnencoded()
    {
        await ClearAsync();
        var untouched = RecordingId.New();

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        EncodeStandingBoard board = await Standing(reading).ReadAsync([untouched], Cancel);

        Assert.Equal(EncodeStanding.NotEncoded, board.For(untouched));
    }

    [Fact]
    public async Task AskingAboutNoRecordingsAtAllAsksTheStoreNothing()
    {
        await ClearAsync();
        var watched = new RecordedCommands();

        await using ServiceProvider watching = Watching(watched);
        await using AsyncServiceScope reading = watching.CreateAsyncScope();
        EncodeStandingBoard board = await Standing(reading).ReadAsync([], Cancel);

        Assert.Empty(watched.Seen);
        Assert.Equal(EncodeStanding.NotEncoded, board.For(RecordingId.New()));
    }

    [Fact(DisplayName = "two artefacts made and a third job running is a recording that stands encoded")]
    public async Task TwoArtefactsMadeAndAThirdJobRunningIsARecordingThatStandsEncoded()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeProfile compatible = await ProfileAsync("Compatible");
        var watched = RecordingId.New();
        var beside = RecordingId.New();

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            IEncodeJobRepository ledger = Jobs(writing);
            await ledger.AddAsync(Finished(watched, profile, destination), Cancel);
            await ledger.AddAsync(Finished(watched, compatible, destination), Cancel);
            await ledger.AddAsync(Running(watched, profile, destination), Cancel);
            await ledger.AddAsync(Finished(beside, profile, destination), Cancel);
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        EncodeStandingBoard board = await Standing(reading).ReadAsync([watched], Cancel);

        Assert.Equal(EncodeStanding.Completed, board.For(watched));
        Assert.Equal(EncodeStanding.NotEncoded, board.For(beside));
    }

    [Fact(DisplayName = "a job called off leaves the recording where it was, and a retry waiting speaks over the failure before it")]
    public async Task AJobCalledOffLeavesTheRecordingWhereItWasAndARetryWaitingSpeaksOverTheFailure()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        var abandoned = RecordingId.New();
        var retried = RecordingId.New();

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            IEncodeJobRepository ledger = Jobs(writing);
            await ledger.AddAsync(CalledOff(abandoned, profile, destination), Cancel);
            await ledger.AddAsync(Broken(retried, profile, destination), Cancel);
            await ledger.AddAsync(Job(retried, profile, destination), Cancel);
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        EncodeStandingBoard board = await Standing(reading).ReadAsync([abandoned, retried], Cancel);

        Assert.Equal(EncodeStanding.NotEncoded, board.For(abandoned));
        Assert.Equal(EncodeStanding.Queued, board.For(retried));
    }

    private async Task ClearAsync()
    {
        await using AsyncServiceScope clearing = container.CreateAsyncScope();
        await Context(clearing).Set<EncodeScratchFile>().ExecuteDeleteAsync(Cancel);
        await Context(clearing).Set<EncodeJob>().ExecuteDeleteAsync(Cancel);
    }

    private async Task<EncodeProfile> ProfileAsync(string label)
    {
        EncodeProfile profile = EncodeProfile.Define(
            EncodeProfileId.New(),
            new EncodeLabel(label),
            EncodeCodec.H264,
            EncodeResolution.Hd,
            Deinterlace.EveryFrame,
            new ConstantRateFactor(22),
            new ConstantQuantiser(25),
            Defined);

        await using AsyncServiceScope writing = container.CreateAsyncScope();
        await Profiles(writing).AddAsync(profile, Cancel);

        return profile;
    }

    private async Task<(EncodeProfile, EncodeDestination)> DefinedAsync()
    {
        EncodeProfile profile = await ProfileAsync("Viewing");
        EncodeDestination destination = EncodeDestination.Define(
            EncodeDestinationId.New(),
            new EncodeLabel("Primary"),
            Primary,
            profile.Id,
            Defined);

        await using AsyncServiceScope writing = container.CreateAsyncScope();
        await Destinations(writing).AddAsync(destination, Cancel);

        return (profile, destination);
    }

    private static EncodeJob Job(RecordingId recording, EncodeProfile profile, EncodeDestination destination)
        => EncodeJob.Queue(EncodeJobId.New(), recording, profile.Id, destination.Id, Primary, Queued);

    private static EncodeJob Running(RecordingId recording, EncodeProfile profile, EncodeDestination destination)
    {
        EncodeJob job = Job(recording, profile, destination);
        job.Start(Started);

        return job;
    }

    private static EncodeJob Finished(RecordingId recording, EncodeProfile profile, EncodeDestination destination)
    {
        EncodeJob job = Running(recording, profile, destination);
        job.Name(EncodeFileName.Artefact(recording, profile.Id));
        job.Complete(Ended);

        return job;
    }

    private static EncodeJob Broken(RecordingId recording, EncodeProfile profile, EncodeDestination destination)
    {
        EncodeJob job = Running(recording, profile, destination);
        job.Fail(EncodeFailure.FfmpegExitedNonZero, "the programme exited 255", Ended);

        return job;
    }

    private static EncodeJob CalledOff(RecordingId recording, EncodeProfile profile, EncodeDestination destination)
    {
        EncodeJob job = Running(recording, profile, destination);
        job.Cancel(Ended);

        return job;
    }

    private static IEncodeDestinationRepository Destinations(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IEncodeDestinationRepository>();

    private static IEncodeJobRepository Jobs(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IEncodeJobRepository>();

    private static IEncodeProfileRepository Profiles(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IEncodeProfileRepository>();

    private static EncodeStandingReader Standing(AsyncServiceScope scope)
        => ActivatorUtilities.CreateInstance<EncodeStandingReader>(scope.ServiceProvider);

    private static CarinaDbContext Context(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<CarinaDbContext>();
}
