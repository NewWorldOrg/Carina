using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Tests.Fixtures.Encodings;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace Carina.Infrastructure.Tests.DbIntegration.Infra.Encodings;

/// <summary>
/// However a job whose process died is put back or given up when the application starts again, the recording it
/// read keeps its result, reasons, size and the time that size was read exactly as they were.
/// </summary>
[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class EncodeRestartTests(RepositoryDatabase database) : IAsyncLifetime
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly RunningProgramme LeftBehind = new(31337, EncodeHarness.Started.AddSeconds(1));

    private readonly ServiceProvider container = database.Infrastructure();

    public Task InitializeAsync() => ClearAsync();

    public async Task DisposeAsync()
    {
        await ClearAsync();
        await container.DisposeAsync();
    }

    [Theory(DisplayName = "a job whose process died, put back or given up, leaves the recording's row exactly as it was")]
    [InlineData(EncodeJob.FirstAttempt, EncodeJobStatus.Queued)]
    [InlineData(3, EncodeJobStatus.Failed)]
    public async Task AJobWhoseProcessDiedLeavesTheRecordingRowAsItWas(int attempt, EncodeJobStatus becomes)
    {
        using var harness = new EncodeHarness();
        (Recording recording, EncodeJob written) = await LedgerAsync(harness, attempt, LeftBehind);
        string before = await RowAsync(recording.Id);

        await using ServiceProvider standingIn = database.Infrastructure(replace =>
        {
            harness.StandIn(replace);
            replace.AddSingleton<IStrayProgrammes>(new ScriptedStrays());
            replace.AddSingleton(new EncodeSettings { MostAttempts = 3 });
        });

        await using (AsyncServiceScope restarting = standingIn.CreateAsyncScope())
        {
            await restarting.ServiceProvider.GetRequiredService<EncodeRestart>().RecoverAsync(Cancel);
        }

        Assert.Equal(before, await RowAsync(recording.Id));

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        Assert.Equal(becomes, (await Jobs(reading).FindAsync(written.Id, Cancel))!.Status);
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
