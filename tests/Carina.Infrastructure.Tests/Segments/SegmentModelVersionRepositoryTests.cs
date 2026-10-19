using Carina.Domain.Channels;
using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests.Segments;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class SegmentModelVersionRepositoryTests(RepositoryDatabase database) : IAsyncLifetime
{
    private const string Ours = "number BETWEEN 61 AND 69";

    private const string OthersOutOfUse =
        $"UPDATE segment_model_version SET state = 'Retired', retired_at = adopted_at, by_hand = false, chosen_by_hand_at = NULL WHERE state = 'Active' AND number <> 1 AND NOT ({Ours})";

    private static readonly DateTime Noon = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Installed = new(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly SegmentEvaluation Evaluated = new(
        new SegmentMetrics(0.93, null, [new KindMetrics(SegmentKind.Break, 0.95), new KindMetrics(SegmentKind.Opening, null)], 0.03, 64),
        [new StationMetrics(new NetworkId(40_001), new ServiceId(60_001), 5, new SegmentMetrics(0.96, 0.9, [], null, 0))],
        new EvaluationEvidence(12, 148, Noon.AddHours(-3)));

    public Task InitializeAsync() => ResetAsync();

    public Task DisposeAsync() => ResetAsync();

    [Fact(DisplayName = "BR-CD-008: the figures of an evaluation and what a rollback measured are kept in the shapes the status answers, and read back as they were")]
    public async Task TheFiguresOfAnEvaluationAreKeptAndReadBack()
    {
        await AddAsync(Version(61, SegmentModelVersionState.Retired, Noon, Noon.AddDays(7), evaluation: Evaluated, rolledBackAt: Noon.AddDays(7), rollback: new SegmentRollback(0.09, 0.03, 25, 1)));

        SegmentModelVersion read = (await ListAsync()).Single(version => version.Number == 61);

        Assert.Equal((0.93, (double?)null, 0.03, 64), (read.Metrics!.BoundaryRecallWithin2s!.Value, read.Metrics.BoundaryRecallWithin1s, read.Metrics.ReturnedAfterSkipShare!.Value, read.Metrics.Skips));
        Assert.Equal([new KindMetrics(SegmentKind.Break, 0.95), new KindMetrics(SegmentKind.Opening, null)], read.Metrics.Kinds);
        StationMetrics station = Assert.Single(read.Stations!);
        Assert.Equal((40_001, 60_001, 5, 0.96, 0.9), (station.NetworkId.Value, station.ServiceId.Value, station.Recordings, station.Metrics.BoundaryRecallWithin2s!.Value, station.Metrics.BoundaryRecallWithin1s!.Value));
        Assert.Equal(new EvaluationEvidence(12, 148, Noon.AddHours(-3)), read.Evidence);
        Assert.Equal(new SegmentRollback(0.09, 0.03, 25, 1), read.Rollback);
        Assert.Equal(
            "t t t t",
            await ScalarAsync(
                """
                SELECT concat_ws(' ',
                    metrics = '{"boundaryRecallWithin2s": 0.93, "boundaryRecallWithin1s": null, "kinds": [{"kind": "Break", "recall": 0.95}, {"kind": "Opening", "recall": null}], "returnedAfterSkipShare": 0.03, "skips": 64}'::jsonb,
                    stations = '[{"networkId": 40001, "serviceId": 60001, "recordings": 5, "metrics": {"boundaryRecallWithin2s": 0.96, "boundaryRecallWithin1s": 0.9, "kinds": [], "returnedAfterSkipShare": null, "skips": 0}}]'::jsonb,
                    evidence = '{"recordings": 12, "boundaries": 148, "labelsUntil": "2026-10-18T09:00:00Z"}'::jsonb,
                    rollback = '{"returnedAfterSkipShare": 0.09, "previousShare": 0.03, "skips": 25, "backTo": 1}'::jsonb)
                FROM segment_model_version WHERE number = 61
                """));
    }

    [Fact(DisplayName = "a version never evaluated is read back with no figures at all, and every version is listed the highest number first")]
    public async Task AVersionNeverEvaluatedIsReadBackWithNoFigures()
    {
        await AddAsync(Version(62, SegmentModelVersionState.Candidate));
        await AddAsync(Version(63, SegmentModelVersionState.Candidate, evaluation: Evaluated));

        IReadOnlyList<SegmentModelVersion> listed = await ListAsync();
        SegmentModelVersion never = listed.Single(version => version.Number == 62);

        Assert.Equal(listed.Select(version => version.Number).OrderDescending(), listed.Select(version => version.Number));
        Assert.Equal([63, 62, 1], listed.Select(version => version.Number));
        Assert.Null(never.Evaluation);
        Assert.Null(never.Rollback);
        Assert.NotNull(listed[0].Evaluation);
    }

    [Fact(DisplayName = "BR-CS-003: choosing a version by hand leaves exactly one version in use: the one chosen, marked so, and the one before it taken out of use")]
    public async Task ChoosingAVersionByHandLeavesExactlyOneInUse()
    {
        await AddAsync(Version(64, SegmentModelVersionState.Candidate, evaluation: Evaluated));

        SegmentModelVersion? active = await ChangeAsync(SegmentVersionChange.ChosenByHand(64), Noon.AddDays(1));
        IReadOnlyList<SegmentModelVersion> listed = await ListAsync();

        Assert.Equal(64, active?.Number);
        Assert.Equal("64", await ScalarAsync("SELECT string_agg(number::text, ' ') FROM segment_model_version WHERE state = 'Active'"));
        Assert.Equal(
            (SegmentModelVersionState.Active, true, (DateTime?)Noon.AddDays(1), (DateTime?)Noon.AddDays(1)),
            listed.Where(version => version.Number == 64).Select(version => (version.State, version.ByHand, version.AdoptedAt, version.ChosenByHandAt)).Single());
        Assert.Equal(
            (SegmentModelVersionState.Retired, false, (DateTime?)Noon.AddDays(1)),
            listed.Where(version => version.Number == SegmentModelVersion.FirstNumber).Select(version => (version.State, version.ByHand, version.RetiredAt)).Single());
        Assert.Equal(64, (await ActiveAsync()).Number);
        Assert.NotNull(listed.Single(version => version.Number == 64).Evaluation);
    }

    [Fact(DisplayName = "BR-CD-008: a rollback is kept on the version it leaves, and the version gone back to is in use again")]
    public async Task ARollbackIsKeptOnTheVersionItLeaves()
    {
        await AddAsync(Version(65, SegmentModelVersionState.Candidate));
        await ChangeAsync(SegmentVersionChange.Adopting(65), Noon.AddDays(1));

        SegmentModelVersion? active = await ChangeAsync(SegmentVersionChange.GoingBack(new SegmentRollback(0.09, 0.03, 25, SegmentModelVersion.FirstNumber)), Noon.AddDays(3));

        Assert.Equal(SegmentModelVersion.FirstNumber, active?.Number);
        SegmentModelVersion left = (await ListAsync()).Single(version => version.Number == 65);
        Assert.Equal((SegmentModelVersionState.Retired, (DateTime?)Noon.AddDays(3)), (left.State, left.RolledBackAt));
        Assert.Equal(new SegmentRollback(0.09, 0.03, 25, SegmentModelVersion.FirstNumber), left.Rollback);
    }

    [Fact(DisplayName = "BR-CS-003: a candidate learning makes is kept under the number after the highest, with its figures in the shapes the status answers, and never in use")]
    public async Task ACandidateIsKeptUnderTheNextNumber()
    {
        await AddAsync(Version(61, SegmentModelVersionState.Candidate));

        SegmentModelVersion kept = await AddCandidateAsync(Noon.AddDays(1));

        Assert.Equal((62, SegmentModelVersionState.Candidate, false, (DateTime?)null), (kept.Number, kept.State, kept.ByHand, kept.AdoptedAt));
        SegmentModelVersion read = (await ListAsync()).Single(version => version.Number == 62);
        Assert.Equal((SegmentModelVersionState.Candidate, Noon.AddDays(1), (DateTime?)null), (read.State, read.TrainedAt, read.AdoptedAt));
        Assert.Equal(Evaluated.Evidence, read.Evidence);
        Assert.Equal(SegmentModel.First.Weights, read.Weights);
        Assert.Equal(
            "Candidate t t t",
            await ScalarAsync(
                """
                SELECT concat_ws(' ', state,
                    metrics = '{"boundaryRecallWithin2s": 0.93, "boundaryRecallWithin1s": null, "kinds": [{"kind": "Break", "recall": 0.95}, {"kind": "Opening", "recall": null}], "returnedAfterSkipShare": 0.03, "skips": 64}'::jsonb,
                    stations = '[{"networkId": 40001, "serviceId": 60001, "recordings": 5, "metrics": {"boundaryRecallWithin2s": 0.96, "boundaryRecallWithin1s": 0.9, "kinds": [], "returnedAfterSkipShare": null, "skips": 0}}]'::jsonb,
                    evidence = '{"recordings": 12, "boundaries": 148, "labelsUntil": "2026-10-18T09:00:00Z"}'::jsonb)
                FROM segment_model_version WHERE number = 62
                """));
        Assert.Equal(SegmentModelVersion.FirstNumber, (await ActiveAsync()).Number);
    }

    [Fact(DisplayName = "BR-CD-008: a candidate kept is put in use by learning through the one change, and the version it takes the place of is taken out of use")]
    public async Task ACandidateKeptIsPutInUseByLearning()
    {
        await AddAsync(Version(61, SegmentModelVersionState.Candidate));
        SegmentModelVersion kept = await AddCandidateAsync(Noon.AddDays(1));

        SegmentModelVersion? active = await ChangeAsync(SegmentVersionChange.Adopting(kept.Number), Noon.AddDays(1).AddMinutes(5));

        Assert.Equal((62, false, (DateTime?)Noon.AddDays(1).AddMinutes(5)), (active?.Number, active?.ByHand, active?.AdoptedAt));
        Assert.Equal("1:Retired:false 61:Candidate:false 62:Active:false", await StatesAsync());
        Assert.Equal(SegmentModelVersion.FirstNumber, SegmentRollback.Previous(await ListAsync())?.Number);
    }

    [Fact(DisplayName = "BR-CS-003: candidates kept at once are each given a number of their own")]
    public async Task CandidatesKeptAtOnceAreNumberedApart()
    {
        await AddAsync(Version(61, SegmentModelVersionState.Candidate));

        using var gate = new Barrier(3);
        int[] numbers = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
        {
            await using CarinaDbContext writing = database.Open();
            await writing.Database.OpenConnectionAsync(Cancel);
            gate.SignalAndWait(Cancel);

            return (await new SegmentModelVersionRepository(writing).AddCandidateAsync(SegmentModel.First, Evaluated, Noon.AddDays(1), Cancel)).Number;
        })));

        Assert.Equal([62, 63, 64], numbers.Order());
    }

    [Fact(DisplayName = "a change naming a number no version has writes nothing")]
    public async Task AChangeNamingAnUnknownNumberWritesNothing()
    {
        string before = await StatesAsync();

        Assert.Null(await ChangeAsync(SegmentVersionChange.ChosenByHand(69), Noon.AddDays(1)));
        Assert.Equal(before, await StatesAsync());
    }

    [Fact(DisplayName = "BR-CS-003: hands choosing versions at once all get through, one after another, and exactly one version is in use after them")]
    public async Task HandsChoosingAtOnceAllGetThrough()
    {
        int[] numbers = [66, 67, 68];

        foreach (int number in numbers)
        {
            await AddAsync(Version(number, SegmentModelVersionState.Candidate));
        }

        using var gate = new Barrier(numbers.Length);
        SegmentModelVersion?[] answered = await Task.WhenAll(numbers.Select(number => Task.Run(async () =>
        {
            await using CarinaDbContext writing = database.Open();
            await writing.Database.OpenConnectionAsync(Cancel);
            gate.SignalAndWait(Cancel);

            return await new SegmentModelVersionRepository(writing).ChangeAsync(SegmentVersionChange.ChosenByHand(number), Noon.AddDays(1), Cancel);
        })));

        Assert.All(answered, Assert.NotNull);
        Assert.Equal("1", await ScalarAsync("SELECT count(*) FROM segment_model_version WHERE state = 'Active'"));
        Assert.Contains((await ActiveAsync()).Number, numbers);
    }

    private static SegmentModelVersion Version(
        int number,
        SegmentModelVersionState state,
        DateTime? adoptedAt = null,
        DateTime? retiredAt = null,
        SegmentEvaluation? evaluation = null,
        DateTime? rolledBackAt = null,
        SegmentRollback? rollback = null)
        => SegmentModelVersion.Rehydrate(number, state, false, SegmentModel.First, Noon.AddHours(-1), adoptedAt, retiredAt, null, rolledBackAt, evaluation, rollback);

    private async Task AddAsync(SegmentModelVersion version)
    {
        await using CarinaDbContext writing = database.Open();
        writing.Add(version);
        await writing.SaveChangesAsync(Cancel);
    }

    private async Task<IReadOnlyList<SegmentModelVersion>> ListAsync()
    {
        await using CarinaDbContext reading = database.Open();

        return [.. (await new SegmentModelVersionRepository(reading).ListAsync(Cancel)).Where(version => version.Number is SegmentModelVersion.FirstNumber or (>= 61 and <= 69))];
    }

    private async Task<SegmentModelVersion> AddCandidateAsync(DateTime trainedAt)
    {
        await using CarinaDbContext writing = database.Open();

        return await new SegmentModelVersionRepository(writing).AddCandidateAsync(SegmentModel.First, Evaluated, trainedAt, Cancel);
    }

    private async Task<SegmentModelVersion> ActiveAsync()
    {
        await using CarinaDbContext reading = database.Open();

        return await new SegmentModelVersionRepository(reading).ActiveAsync(Cancel);
    }

    private async Task<SegmentModelVersion?> ChangeAsync(SegmentVersionChange change, DateTime at)
    {
        await using CarinaDbContext writing = database.Open();

        return await new SegmentModelVersionRepository(writing).ChangeAsync(change, at, Cancel);
    }

    private Task<string> StatesAsync() => ScalarAsync($"SELECT string_agg(number || ':' || state || ':' || by_hand, ' ' ORDER BY number) FROM segment_model_version WHERE number = 1 OR {Ours}");

    private async Task<string> ScalarAsync(string sql)
    {
        await using CarinaDbContext reading = database.Open();
        await reading.Database.OpenConnectionAsync(Cancel);
        await using System.Data.Common.DbCommand command = reading.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;

        return Convert.ToString(await command.ExecuteScalarAsync(Cancel), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    /// <summary>
    /// Leaves the first version alone in use with the first model, the version of four kinds the migrations put in
    /// use beside it as one used before, whatever other tests keep, and none of the versions these tests add.
    /// </summary>
    private async Task ResetAsync()
    {
        await using CarinaDbContext context = database.Open();
        await context.Database.ExecuteSqlRawAsync($"DELETE FROM segment_model_version WHERE {Ours}", Cancel);
        await context.Database.ExecuteSqlRawAsync(OthersOutOfUse, Cancel);
        SegmentModelVersion first = SegmentModelVersion.Rehydrate(SegmentModelVersion.FirstNumber, SegmentModelVersionState.Active, false, SegmentModel.First, Installed, Installed, null, null, null);

        int restored = await context.Set<SegmentModelVersion>()
            .Where(version => version.Number == SegmentModelVersion.FirstNumber)
            .ExecuteUpdateAsync(
                version => version
                    .SetProperty(held => held.State, first.State)
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
            context.Add(first);
            await context.SaveChangesAsync(Cancel);
        }
    }
}
