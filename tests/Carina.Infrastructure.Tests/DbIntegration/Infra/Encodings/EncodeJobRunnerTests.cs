using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Tests.Fixtures.Encodings;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace Carina.Infrastructure.Tests.DbIntegration.Infra.Encodings;

/// <summary>
/// However a job run over the ledger ends, the recording it read keeps its result, reasons, size and the time
/// that size was read exactly as they were. The row is read back whole, with the version the store keeps for it.
/// </summary>
[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class EncodeJobRunnerTests(RepositoryDatabase database) : IAsyncLifetime
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ServiceProvider container = database.Infrastructure();

    public Task InitializeAsync() => ClearAsync();

    public async Task DisposeAsync()
    {
        await ClearAsync();
        await container.DisposeAsync();
    }

    [Fact(DisplayName = "a job whose programme fails leaves the recording's row exactly as it was")]
    public async Task AJobWhoseProgrammeFailsLeavesTheRecordingRowAsItWas()
    {
        using var harness = new EncodeHarness();
        harness.Standing("""
            echo "$destination: Invalid data found when processing input" >&2
            exit 187
            """);
        (Recording recording, EncodeJob written) = await LedgerAsync(harness);
        string before = await RowAsync(recording.Id);

        await using ServiceProvider standingIn = StandingIn(harness);
        await using AsyncServiceScope running = standingIn.CreateAsyncScope();
        EncodeJob job = (await Jobs(running).FindAsync(written.Id, Cancel))!;

        EncodeJobStatus ended = await Runner(running).RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Failed, ended);
        Assert.Equal(before, await RowAsync(recording.Id));
    }

    [Fact(DisplayName = "a job refused before its programme starts leaves the recording's row exactly as it was")]
    public async Task AJobRefusedBeforeItsProgrammeStartsLeavesTheRecordingRowAsItWas()
    {
        using var harness = new EncodeHarness();
        harness.Standing("exit 0");
        harness.Heads.Reading = SourceHeadReading.Read(MeasuredHeads.Start, MeasuredHeads.Start + TimeSpan.FromSeconds(9));
        (Recording recording, EncodeJob written) = await LedgerAsync(harness);
        string before = await RowAsync(recording.Id);

        await using ServiceProvider standingIn = StandingIn(harness);
        await using AsyncServiceScope running = standingIn.CreateAsyncScope();
        EncodeJob job = (await Jobs(running).FindAsync(written.Id, Cancel))!;

        EncodeJobStatus ended = await Runner(running).RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Failed, ended);
        Assert.Equal(EncodeFailure.HeadTooFar, job.Failure!.Failure);
        Assert.Equal(before, await RowAsync(recording.Id));
    }

    [Fact(DisplayName = "a job called off while it runs leaves the recording's row exactly as it was")]
    public async Task AJobCalledOffWhileItRunsLeavesTheRecordingRowAsItWas()
    {
        using var harness = new EncodeHarness();
        harness.Standing("exit 187");
        (Recording recording, EncodeJob written) = await LedgerAsync(harness);
        string before = await RowAsync(recording.Id);

        await using ServiceProvider standingIn = StandingIn(harness);
        await using AsyncServiceScope running = standingIn.CreateAsyncScope();
        EncodeJob job = (await Jobs(running).FindAsync(written.Id, Cancel))!;

        await using (AsyncServiceScope callingOff = container.CreateAsyncScope())
        {
            IEncodeJobRepository jobs = Jobs(callingOff);
            EncodeJob held = (await jobs.FindAsync(written.Id, Cancel))!;
            held.Cancel(harness.Clock.GetUtcNow().UtcDateTime);
            await jobs.SaveAsync(held, Cancel);
        }

        Exception? ended = await Record.ExceptionAsync(() => Runner(running).RunAsync(job, Cancel));

        Assert.IsType<EncodeJobMovedMeanwhileException>(ended);
        Assert.Equal(before, await RowAsync(recording.Id));

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        Assert.Equal(
            EncodeJobStatus.Cancelled,
            (await Jobs(reading).FindAsync(written.Id, Cancel))!.Status);
    }

    private async Task<(Recording Recording, EncodeJob Job)> LedgerAsync(
        EncodeHarness harness,
        int attempt = EncodeJob.FirstAttempt,
        RunningProgramme? programme = null)
    {
        Recording recording = harness.Recorded();
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
            EncodeJobStatus.Running,
            attempt,
            EncodeHarness.Queued,
            EncodeHarness.Started,
            null,
            null,
            null,
            null,
            programme,
            null,
            null,
            null);

        await using AsyncServiceScope writing = container.CreateAsyncScope();
        await Recordings(writing).AddAsync(recording, Cancel);
        await Profiles(writing).AddAsync(profile, Cancel);
        await Destinations(writing).AddAsync(destination, Cancel);
        await Jobs(writing).AddAsync(job, Cancel);

        return (recording, job);
    }

    private ServiceProvider StandingIn(EncodeHarness harness)
        => database.Infrastructure(replace =>
        {
            harness.StandIn(replace);
            replace.AddSingleton<ISegmentModelVersionRepository>(harness.Versions);
            replace.AddSingleton<IRecordingDetectionRepository>(harness.Detections);
        });

    private static EncodeJobRunner Runner(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<EncodeJobRunner>();

    private async Task<string> RowAsync(RecordingId id)
    {
        await using AsyncServiceScope reading = container.CreateAsyncScope();
        var connection = (NpgsqlConnection)Context(reading).Database.GetDbConnection();
        await connection.OpenAsync(Cancel);

        await using var asking = new NpgsqlCommand(
            "SELECT xmin::text || ' ' || to_jsonb(recording)::text FROM recording WHERE id = @id",
            connection);
        asking.Parameters.AddWithValue("id", id.Value);

        return (string)(await asking.ExecuteScalarAsync(Cancel))!;
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
