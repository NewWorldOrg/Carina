using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

namespace Carina.Infrastructure.Tests.Encodings;

/// <summary>
/// Encoding is downstream of recording: however a job ends, the recording it read keeps the
/// result, the reasons, the size and the time that size was read exactly as they were. The row is
/// read back whole, with the version the store keeps for it, so a write that changed nothing is
/// caught as surely as one that changed something.
/// </summary>
[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class EncodeEndsLeaveTheRecordingAsItWasTests(RepositoryDatabase database) : IAsyncLifetime
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly RunningProgramme LeftBehind = new(31337, EncodeHarness.Started.AddSeconds(1));

    public Task InitializeAsync() => ClearAsync();

    public Task DisposeAsync() => ClearAsync();

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

        await using CarinaDbContext running = database.Open();
        EncodeJob job = (await new EncodeJobRepository(running).FindAsync(written.Id, Cancel))!;

        EncodeJobStatus ended = await Runner(harness, running).RunAsync(job, Cancel);

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

        await using CarinaDbContext running = database.Open();
        EncodeJob job = (await new EncodeJobRepository(running).FindAsync(written.Id, Cancel))!;

        EncodeJobStatus ended = await Runner(harness, running).RunAsync(job, Cancel);

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

        await using CarinaDbContext running = database.Open();
        EncodeJob job = (await new EncodeJobRepository(running).FindAsync(written.Id, Cancel))!;

        await using (CarinaDbContext callingOff = database.Open())
        {
            var jobs = new EncodeJobRepository(callingOff);
            EncodeJob held = (await jobs.FindAsync(written.Id, Cancel))!;
            held.Cancel(harness.Clock.GetUtcNow().UtcDateTime);
            await jobs.SaveAsync(held, Cancel);
        }

        Exception? ended = await Record.ExceptionAsync(() => Runner(harness, running).RunAsync(job, Cancel));

        Assert.IsType<EncodeJobMovedMeanwhileException>(ended);
        Assert.Equal(before, await RowAsync(recording.Id));

        await using CarinaDbContext reading = database.Open();
        Assert.Equal(
            EncodeJobStatus.Cancelled,
            (await new EncodeJobRepository(reading).FindAsync(written.Id, Cancel))!.Status);
    }

    [Theory(DisplayName = "a job whose process died, put back or given up, leaves the recording's row exactly as it was")]
    [InlineData(EncodeJob.FirstAttempt, EncodeJobStatus.Queued)]
    [InlineData(3, EncodeJobStatus.Failed)]
    public async Task AJobWhoseProcessDiedLeavesTheRecordingRowAsItWas(int attempt, EncodeJobStatus becomes)
    {
        using var harness = new EncodeHarness();
        (Recording recording, EncodeJob written) = await LedgerAsync(harness, attempt, LeftBehind);
        string before = await RowAsync(recording.Id);

        await using (CarinaDbContext restarting = database.Open())
        {
            await new EncodeRestart(
                    new EncodeJobRepository(restarting),
                    new ScriptedStrays(),
                    new EncodeScratchCleaner(
                        new EncodeScratchLedger(restarting),
                        harness.Places,
                        harness.Clock,
                        NullLogger<EncodeScratchCleaner>.Instance),
                    new EncodeSettings { MostAttempts = 3 },
                    harness.Clock,
                    NullLogger<EncodeRestart>.Instance)
                .RecoverAsync(Cancel);
        }

        Assert.Equal(before, await RowAsync(recording.Id));

        await using CarinaDbContext reading = database.Open();
        Assert.Equal(becomes, (await new EncodeJobRepository(reading).FindAsync(written.Id, Cancel))!.Status);
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

        await using CarinaDbContext writing = database.Open();
        await new RecordingRepository(writing).AddAsync(recording, Cancel);
        await new EncodeProfileRepository(writing).AddAsync(profile, Cancel);
        await new EncodeDestinationRepository(writing).AddAsync(destination, Cancel);
        await new EncodeJobRepository(writing).AddAsync(job, Cancel);

        return (recording, job);
    }

    private static EncodeJobRunner Runner(EncodeHarness harness, CarinaDbContext context)
        => new(
            new EncodeJobRepository(context),
            new EncodeProfileRepository(context),
            new RecordingRepository(context),
            harness.Places,
            harness.ScratchFiles,
            harness.Placer,
            harness.Cleaner,
            harness.MachineReader,
            harness.LengthReader,
            harness.HeadReader,
            harness.ChapterDetector,
            harness.Chapters,
            new StationWatermarkRepository(context),
            harness.Programmes,
            harness.Settings,
            harness.AutoRun,
            harness.Clock,
            harness.RunnerLog);

    private async Task<string> RowAsync(RecordingId id)
    {
        await using CarinaDbContext reading = database.Open();
        var connection = (NpgsqlConnection)reading.Database.GetDbConnection();
        await connection.OpenAsync(Cancel);

        await using var asking = new NpgsqlCommand(
            "SELECT xmin::text || ' ' || to_jsonb(recording)::text FROM recording WHERE id = @id",
            connection);
        asking.Parameters.AddWithValue("id", id.Value);

        return (string)(await asking.ExecuteScalarAsync(Cancel))!;
    }

    private async Task ClearAsync()
    {
        await using CarinaDbContext clearing = database.Open();
        await clearing.Set<EncodeChapter>().ExecuteDeleteAsync(Cancel);
        await clearing.Set<EncodeScratchFile>().ExecuteDeleteAsync(Cancel);
        await clearing.Set<EncodeJob>().ExecuteDeleteAsync(Cancel);
    }
}
