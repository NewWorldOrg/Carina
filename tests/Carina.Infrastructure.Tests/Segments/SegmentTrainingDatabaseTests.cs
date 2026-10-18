using System.Globalization;

using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.Infrastructure.Segments;
using Carina.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.Segments;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class SegmentTrainingDatabaseTests : IAsyncLifetime, IDisposable
{
    private const string Ours = "number BETWEEN 71 AND 79";

    private const string OthersOutOfUse =
        $"UPDATE segment_model_version SET state = 'Retired', retired_at = adopted_at, by_hand = false, chosen_by_hand_at = NULL WHERE state = 'Active' AND number <> 1 AND NOT ({Ours})";

    private static readonly DateTime Installed = new(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly RepositoryDatabase database;

    private readonly ServiceProvider provider;

    private readonly HandTurnedClock clock = new(new DateTimeOffset(SegmentTrainingHarness.Start));

    private readonly HeldTrainer trainer = new();

    private readonly List<Guid> skipped = [];

    public SegmentTrainingDatabaseTests(RepositoryDatabase database)
    {
        this.database = database;
        provider = new ServiceCollection()
            .AddScoped(_ => database.Open())
            .AddScoped<ISegmentModelVersionRepository, SegmentModelVersionRepository>()
            .AddScoped<ISegmentLearningRoundRepository, SegmentLearningRoundRepository>()
            .AddScoped<ILearningMaterialChanges, LearningMaterialChanges>()
            .AddScoped<ISkipEventRepository, SkipEventRepository>()
            .AddScoped<ISegmentSettingsRepository, SegmentSettingsRepository>()
            .AddScoped<ILearningSwitch, LearningSwitch>()
            .AddScoped<IOccupancyReader, OccupancyReader>()
            .AddSingleton<IBusynessReader>(new HeldBusyness(() => new Busyness(false, false, null)))
            .AddSingleton<ISegmentTrainer>(trainer)
            .BuildServiceProvider();
    }

    public async Task InitializeAsync()
    {
        await ResetAsync();
        await using CarinaDbContext writing = database.Open();
        await new SegmentSettingsRepository(writing).SaveAsync(SegmentSettings.LearningSwitched(true, Installed), Cancel);
        writing.Add(SegmentModelVersion.Learned(71, SegmentModel.First, SegmentTrainingHarness.Evaluated(0.5, 3, 4), Installed.AddHours(1)));
        await writing.SaveChangesAsync(Cancel);
    }

    public Task DisposeAsync() => ResetAsync();

    public void Dispose() => provider.Dispose();

    [Fact(DisplayName = "BR-CD-008: the day's round keeps its candidate in the store with its figures, puts it in use through the one change, and keeps the day so that a job started again does not take it again")]
    public async Task TheDaysRoundKeepsItsCandidateAndPutsItInUse()
    {
        trainer.Candidate = SegmentTrainingHarness.Evaluated(0.93, 1.0, 1.5);

        SegmentTrainingLook look = await Job().LookAsync(Cancel);
        SegmentTrainingLook again = await Job().LookAsync(Cancel);

        Assert.Equal((SegmentTrainingOutcome.Adopted, (int?)72, 72), (look.Round?.Outcome, look.Round?.Candidate?.Number, look.Round?.Active.Number));
        Assert.Equal(new SegmentTrainingLook(null, null), again);
        Assert.Equal(
            "1:Retired 71:Candidate 72:Active",
            await ScalarAsync($"SELECT string_agg(number || ':' || state, ' ' ORDER BY number) FROM segment_model_version WHERE number = 1 OR {Ours}"));
        Assert.Equal(
            "t t t 0.65 t",
            await ScalarAsync(
                """
                SELECT concat_ws(' ',
                    metrics = '{"boundaryRecallWithin2s": 0.93, "boundaryRecallWithin1s": 0.83, "extraBoundariesPerRecording": 1, "programmeSkippedSecondsPerHour": 1.5, "kinds": [{"kind": "Break", "recall": 0.9, "precision": 0.9}], "returnedAfterSkipShare": null, "skips": 0}'::jsonb,
                    stations = '[]'::jsonb,
                    evidence = '{"confirmedRecordings": 12, "boundaries": 148, "labelsUntil": "2026-11-01T12:30:00Z"}'::jsonb,
                    recommended_threshold,
                    adopted_at >= trained_at)
                FROM segment_model_version WHERE number = 72
                """));
        Assert.Equal(
            "2026-11-02 t",
            await ScalarAsync("SELECT concat_ws(' ', day::text, taken_at = '2026-11-01T18:30:00Z'::timestamptz) FROM segment_learning_round"));
    }

    [Fact(DisplayName = "BR-CD-004: two rounds on days apart with nothing learning reads changed between them keep one candidate, and the second round is still taken")]
    public async Task TwoRoundsOnUnchangedTablesKeepOneCandidate()
    {
        HandTurnedClock later = new(new DateTimeOffset(2036, 1, 1, 18, 30, 0, TimeSpan.Zero));
        trainer.Candidate = SegmentTrainingHarness.Evaluated(0.80, 1.0, 1.5);

        SegmentTrainingLook first = await Job(later).LookAsync(Cancel);
        later.Turn(TimeSpan.FromDays(1));
        SegmentTrainingLook second = await Job(later).LookAsync(Cancel);

        Assert.Equal((SegmentTrainingOutcome.Kept, SegmentTrainingOutcome.NothingNew), (first.Round?.Outcome, second.Round?.Outcome));
        Assert.Equal("72", await ScalarAsync($"SELECT string_agg(number::text, ' ') FROM segment_model_version WHERE {Ours} AND number <> 71"));
        Assert.Equal(
            "2036-01-02:72 2036-01-03:",
            await ScalarAsync("SELECT string_agg(day::text || ':' || coalesce(candidate::text, ''), ' ' ORDER BY day) FROM segment_learning_round"));
        Assert.Single(trainer.Asked);
    }

    [Fact(DisplayName = "BR-CD-008: a rollback counted from the skip events kept goes back through the one change and is kept on the version left")]
    public async Task ARollbackCountedFromTheStoreGoesBack()
    {
        DateTime adopted = SegmentTrainingHarness.Start.AddDays(-3);
        await using (CarinaDbContext writing = database.Open())
        {
            SegmentModelVersionRepository versions = new(writing);
            await versions.ChangeAsync(SegmentVersionChange.Adopting(71), adopted, Cancel);
        }

        await SkippedAsync(71, adopted.AddHours(1), skips: 25, returned: 10);
        await SkippedAsync(SegmentModelVersion.FirstNumber, adopted.AddDays(-3), skips: 40, returned: 4);

        SegmentTrainingLook look = await Job().LookAsync(Cancel);

        Assert.Equal(new SegmentRollback(0.4, 0.1, 25, SegmentModelVersion.FirstNumber), look.Round?.Rollback);
        Assert.Equal(
            "1:Active 71:Retired:kept",
            await ScalarAsync(
                """
                SELECT string_agg(concat_ws(':', number, state, CASE WHEN rollback = '{"returnedAfterSkipShare": 0.4, "previousShare": 0.1, "skips": 25, "backTo": 1}'::jsonb THEN 'kept' END), ' ' ORDER BY number)
                FROM segment_model_version
                WHERE number = 1 OR number BETWEEN 71 AND 79
                """));
    }

    private SegmentTrainingJob Job(TimeProvider? at = null)
        => new(provider.GetRequiredService<IServiceScopeFactory>(), SegmentTrainingSettings.Default, at ?? clock, new KeptLines<SegmentTrainingJob>());

    private async Task SkippedAsync(int version, DateTime from, int skips, int returned)
    {
        await using CarinaDbContext writing = database.Open();
        SkipEventRepository events = new(writing);
        RecordingId recording = RecordingId.New();
        skipped.Add(recording.Value);

        for (int at = 0; at < skips + returned; at++)
        {
            ViewerActionKind action = at < skips ? ViewerActionKind.Skipped : ViewerActionKind.Returned;

            await events.TakeAsync(
                SkipEvent.Of(recording, new ViewerAction(from.AddMinutes(at), action, SegmentKind.Break, TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(690), version)),
                Cancel);
        }
    }

    private async Task<string> ScalarAsync(string sql)
    {
        await using CarinaDbContext reading = database.Open();
        await reading.Database.OpenConnectionAsync(Cancel);
        await using System.Data.Common.DbCommand command = reading.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;

        return Convert.ToString(await command.ExecuteScalarAsync(Cancel), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    /// <summary>
    /// Leaves the first version alone in use with the first model, the version of four kinds the migrations put in
    /// use beside it as one used before, no versions, skips or rounds of these tests, and no settings row.
    /// </summary>
    private async Task ResetAsync()
    {
        await using CarinaDbContext context = database.Open();
        Guid[] ours = [.. skipped];
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM segment_skip_event WHERE model_version BETWEEN 71 AND 79 OR recording_id = ANY({ours})", Cancel);
        await context.Database.ExecuteSqlRawAsync($"DELETE FROM segment_model_version WHERE {Ours}", Cancel);
        await context.Database.ExecuteSqlRawAsync(OthersOutOfUse, Cancel);
        await context.Database.ExecuteSqlRawAsync("DELETE FROM segment_learning_round", Cancel);
        await context.Database.ExecuteSqlRawAsync("DELETE FROM segment_settings", Cancel);

        SegmentModel first = SegmentModel.First;
        int restored = await context.Set<SegmentModelVersion>()
            .Where(version => version.Number == SegmentModelVersion.FirstNumber)
            .ExecuteUpdateAsync(
                version => version
                    .SetProperty(held => held.State, SegmentModelVersionState.Active)
                    .SetProperty(held => held.ByHand, false)
                    .SetProperty(held => held.Weights, first.Weights)
                    .SetProperty(held => held.StationOffsets, first.StationOffsets)
                    .SetProperty(held => held.SeriesOffsets, first.SeriesOffsets)
                    .SetProperty(held => held.Thresholds, first.Thresholds)
                    .SetProperty(held => held.Standardisation, first.Standardisation)
                    .SetProperty(held => held.TrainedAt, Installed)
                    .SetProperty(held => held.AdoptedAt, Installed)
                    .SetProperty(held => held.RetiredAt, (DateTime?)null)
                    .SetProperty(held => held.ChosenByHandAt, (DateTime?)null)
                    .SetProperty(held => held.RolledBackAt, (DateTime?)null)
                    .SetProperty(held => held.Rollback, (SegmentRollback?)null),
                Cancel);

        if (restored is 0)
        {
            context.Add(SegmentModelVersion.Rehydrate(SegmentModelVersion.FirstNumber, SegmentModelVersionState.Active, false, SegmentModel.First, Installed, Installed, null, null, null));
            await context.SaveChangesAsync(Cancel);
        }
    }
}
