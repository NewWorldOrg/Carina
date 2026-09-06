using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests.Encodings;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class EncodeStandingReaderTests(RepositoryDatabase database)
{
    private const int APageOfTheLibrary = 50;

    private static readonly DateTime Defined = new(2026, 9, 6, 2, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Queued = new(2026, 9, 6, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Started = new(2026, 9, 6, 3, 0, 5, DateTimeKind.Utc);

    private static readonly DateTime Ended = new(2026, 9, 6, 4, 0, 0, DateTimeKind.Utc);

    private static readonly OutputRoot Primary = new("primary");

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-ES-002: a whole page of the library is answered by one look at the ledger")]
    public async Task AWholePageOfTheLibraryIsAnsweredByOneLookAtTheLedger()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        RecordingId[] page = [.. Enumerable.Range(0, APageOfTheLibrary).Select(_ => RecordingId.New())];

        await using (CarinaDbContext writing = database.Open())
        {
            var ledger = new EncodeJobRepository(writing);

            foreach (RecordingId recording in page)
            {
                await ledger.AddAsync(Finished(recording, profile, destination), Cancel);
            }
        }

        var watched = new RecordedCommands();

        await using CarinaDbContext reading = database.Open(watched);
        EncodeStandingBoard board = await new EncodeStandingReader(reading).ReadAsync(page, Cancel);

        Assert.Single(watched.Seen);
        Assert.All(page, recording => Assert.Equal(EncodeStanding.Completed, board.For(recording)));
    }

    [Fact(DisplayName = "BR-ES-002: a recording the ledger holds nothing for stands unencoded rather than missing")]
    public async Task ARecordingTheLedgerHoldsNothingForStandsUnencoded()
    {
        await ClearAsync();
        var untouched = RecordingId.New();

        await using CarinaDbContext reading = database.Open();
        EncodeStandingBoard board = await new EncodeStandingReader(reading).ReadAsync([untouched], Cancel);

        Assert.Equal(EncodeStanding.NotEncoded, board.For(untouched));
    }

    [Fact]
    public async Task AskingAboutNoRecordingsAtAllAsksTheStoreNothing()
    {
        await ClearAsync();
        var watched = new RecordedCommands();

        await using CarinaDbContext reading = database.Open(watched);
        EncodeStandingBoard board = await new EncodeStandingReader(reading).ReadAsync([], Cancel);

        Assert.Empty(watched.Seen);
        Assert.Equal(EncodeStanding.NotEncoded, board.For(RecordingId.New()));
    }

    [Fact(DisplayName = "BR-ES-002: two artefacts made and a third job running is a recording that stands encoded")]
    public async Task TwoArtefactsMadeAndAThirdJobRunningIsARecordingThatStandsEncoded()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeProfile compatible = await ProfileAsync("Compatible");
        var watched = RecordingId.New();
        var beside = RecordingId.New();

        await using (CarinaDbContext writing = database.Open())
        {
            var ledger = new EncodeJobRepository(writing);
            await ledger.AddAsync(Finished(watched, profile, destination), Cancel);
            await ledger.AddAsync(Finished(watched, compatible, destination), Cancel);
            await ledger.AddAsync(Running(watched, profile, destination), Cancel);
            await ledger.AddAsync(Finished(beside, profile, destination), Cancel);
        }

        await using CarinaDbContext reading = database.Open();
        EncodeStandingBoard board = await new EncodeStandingReader(reading).ReadAsync([watched], Cancel);

        Assert.Equal(EncodeStanding.Completed, board.For(watched));
        Assert.Equal(EncodeStanding.NotEncoded, board.For(beside));
    }

    [Fact(DisplayName = "BR-ES-002: a job called off leaves the recording where it was, and a retry waiting speaks over the failure before it")]
    public async Task AJobCalledOffLeavesTheRecordingWhereItWasAndARetryWaitingSpeaksOverTheFailure()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        var abandoned = RecordingId.New();
        var retried = RecordingId.New();

        await using (CarinaDbContext writing = database.Open())
        {
            var ledger = new EncodeJobRepository(writing);
            await ledger.AddAsync(CalledOff(abandoned, profile, destination), Cancel);
            await ledger.AddAsync(Broken(retried, profile, destination), Cancel);
            await ledger.AddAsync(Job(retried, profile, destination), Cancel);
        }

        await using CarinaDbContext reading = database.Open();
        EncodeStandingBoard board = await new EncodeStandingReader(reading).ReadAsync([abandoned, retried], Cancel);

        Assert.Equal(EncodeStanding.NotEncoded, board.For(abandoned));
        Assert.Equal(EncodeStanding.Queued, board.For(retried));
    }

    private async Task ClearAsync()
    {
        await using CarinaDbContext clearing = database.Open();
        await clearing.Set<EncodeScratchFile>().ExecuteDeleteAsync(Cancel);
        await clearing.Set<EncodeJob>().ExecuteDeleteAsync(Cancel);
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

        await using CarinaDbContext writing = database.Open();
        await new EncodeProfileRepository(writing).AddAsync(profile, Cancel);

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

        await using CarinaDbContext writing = database.Open();
        await new EncodeDestinationRepository(writing).AddAsync(destination, Cancel);

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
}
