using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;
using Carina.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using static Carina.Infrastructure.Tests.DbIntegration.Domain.Segments.RowsNamingAVersion;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Segments;

[Trait("Category", "DbIntegration")]
public sealed class SegmentModelVersionRepositoryTests(MigratedRepositoryDatabase database)
    : IClassFixture<MigratedRepositoryDatabase>, IAsyncLifetime
{
    private const int Latest = 12;

    private static readonly DateTime Noon = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Installed = new(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime FirstStarted = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Started = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);

    private static readonly SegmentEvaluation Unmeasured = new(null, [], new EvaluationEvidence(12, 148, FirstStarted));

    private static readonly SegmentEvaluation MeasuredAtFirstStart = new(
        new SegmentMetrics(0.5, 0.4, [new KindMetrics(SegmentKind.Break, 0.8)], null, 0),
        [],
        new EvaluationEvidence(12, 56, FirstStarted));

    private static readonly SegmentEvaluation Evaluated = new(
        new SegmentMetrics(0.93, null, [new KindMetrics(SegmentKind.Break, 0.95), new KindMetrics(SegmentKind.Opening, null)], 0.03, 64),
        [new StationMetrics(new NetworkId(40_001), new ServiceId(60_001), 5, new SegmentMetrics(0.96, 0.9, [], null, 0))],
        new EvaluationEvidence(12, 148, Noon.AddHours(-3)));

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ServiceProvider container = database.Infrastructure();

    public Task InitializeAsync() => ExecuteAsync(Emptied);

    public async Task DisposeAsync() => await container.DisposeAsync();

    [Fact(DisplayName = "BR-CD-008: the figures of an evaluation and what a rollback measured are kept in the shapes the status answers, and read back as they were")]
    public async Task TheFiguresOfAnEvaluationAreKeptAndReadBack()
    {
        await KeepFirstInUseAsync();
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
                    evidence = '{"recordings": 12, "boundaries": 148, "labelsUntil": "2026-10-18T09:00:00Z", "openings": 0, "endings": 0}'::jsonb,
                    rollback = '{"returnedAfterSkipShare": 0.09, "previousShare": 0.03, "skips": 25, "backTo": 1}'::jsonb)
                FROM segment_model_version WHERE number = 61
                """));
    }

    [Fact(DisplayName = "a version never evaluated is read back with no figures at all, and every version is listed the highest number first")]
    public async Task AVersionNeverEvaluatedIsReadBackWithNoFigures()
    {
        await KeepFirstInUseAsync();
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
        await KeepFirstInUseAsync();
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
        await KeepFirstInUseAsync();
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
        await KeepFirstInUseAsync();
        await AddAsync(Version(61, SegmentModelVersionState.Candidate));

        SegmentModelVersion kept = await AddCandidateAsync(Noon.AddDays(1));

        Assert.Equal((62, SegmentModelVersionState.Candidate, false, (DateTime?)null), (kept.Number, kept.State, kept.ByHand, kept.AdoptedAt));
        SegmentModelVersion read = (await ListAsync()).Single(version => version.Number == 62);
        Assert.Equal((SegmentModelVersionState.Candidate, Noon.AddDays(1), (DateTime?)null), (read.State, read.TrainedAt, read.AdoptedAt));
        Assert.Equal(Evaluated.Evidence, read.Evidence);
        Assert.Equal(HandMadeModels.First.Weights, read.Weights);
        Assert.Equal(
            "Candidate t t t",
            await ScalarAsync(
                """
                SELECT concat_ws(' ', state,
                    metrics = '{"boundaryRecallWithin2s": 0.93, "boundaryRecallWithin1s": null, "kinds": [{"kind": "Break", "recall": 0.95}, {"kind": "Opening", "recall": null}], "returnedAfterSkipShare": 0.03, "skips": 64}'::jsonb,
                    stations = '[{"networkId": 40001, "serviceId": 60001, "recordings": 5, "metrics": {"boundaryRecallWithin2s": 0.96, "boundaryRecallWithin1s": 0.9, "kinds": [], "returnedAfterSkipShare": null, "skips": 0}}]'::jsonb,
                    evidence = '{"recordings": 12, "boundaries": 148, "labelsUntil": "2026-10-18T09:00:00Z", "openings": 0, "endings": 0}'::jsonb)
                FROM segment_model_version WHERE number = 62
                """));
        Assert.Equal(SegmentModelVersion.FirstNumber, (await ActiveAsync()).Number);
    }

    [Fact(DisplayName = "BR-CD-008: a candidate kept is put in use by learning through the one change, and the version it takes the place of is taken out of use")]
    public async Task ACandidateKeptIsPutInUseByLearning()
    {
        await KeepFirstInUseAsync();
        await AddAsync(Version(61, SegmentModelVersionState.Candidate));
        SegmentModelVersion kept = await AddCandidateAsync(Noon.AddDays(1));

        SegmentModelVersion? active = await ChangeAsync(SegmentVersionChange.Adopting(kept.Number), Noon.AddDays(1).AddMinutes(5));

        Assert.Equal((62, false, (DateTime?)Noon.AddDays(1).AddMinutes(5)), (active?.Number, active?.ByHand, active?.AdoptedAt));
        Assert.Equal("1:Retired:false 61:Candidate:false 62:Active:false", await StatesAndHandsAsync());
        Assert.Equal(SegmentModelVersion.FirstNumber, SegmentRollback.Previous(await ListAsync())?.Number);
    }

    [Fact(DisplayName = "BR-CS-003: candidates kept at once are each given a number of their own")]
    public async Task CandidatesKeptAtOnceAreNumberedApart()
    {
        await KeepFirstInUseAsync();
        await AddAsync(Version(61, SegmentModelVersionState.Candidate));

        using var gate = new Barrier(3);
        int[] numbers = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
        {
            await using AsyncServiceScope scope = container.CreateAsyncScope();
            CarinaDbContext writing = scope.ServiceProvider.GetRequiredService<CarinaDbContext>();
            await writing.Database.OpenConnectionAsync(Cancel);
            gate.SignalAndWait(Cancel);

            return (await Versions(scope).AddCandidateAsync(HandMadeModels.First, Evaluated, Noon.AddDays(1), Cancel)).Number;
        })));

        Assert.Equal([62, 63, 64], numbers.Order());
    }

    [Fact(DisplayName = "a change naming a number no version has writes nothing")]
    public async Task AChangeNamingAnUnknownNumberWritesNothing()
    {
        await KeepFirstInUseAsync();
        string before = await StatesAndHandsAsync();

        Assert.Null(await ChangeAsync(SegmentVersionChange.ChosenByHand(69), Noon.AddDays(1)));
        Assert.Equal(before, await StatesAndHandsAsync());
    }

    [Fact(DisplayName = "BR-CS-003: hands choosing versions at once all get through, one after another, and exactly one version is in use after them")]
    public async Task HandsChoosingAtOnceAllGetThrough()
    {
        await KeepFirstInUseAsync();
        int[] numbers = [66, 67, 68];

        foreach (int number in numbers)
        {
            await AddAsync(Version(number, SegmentModelVersionState.Candidate));
        }

        using var gate = new Barrier(numbers.Length);
        SegmentModelVersion?[] answered = await Task.WhenAll(numbers.Select(number => Task.Run(async () =>
        {
            await using AsyncServiceScope scope = container.CreateAsyncScope();
            CarinaDbContext writing = scope.ServiceProvider.GetRequiredService<CarinaDbContext>();
            await writing.Database.OpenConnectionAsync(Cancel);
            gate.SignalAndWait(Cancel);

            return await Versions(scope).ChangeAsync(SegmentVersionChange.ChosenByHand(number), Noon.AddDays(1), Cancel);
        })));

        Assert.All(answered, Assert.NotNull);
        Assert.Equal("1", await ScalarAsync("SELECT count(*) FROM segment_model_version WHERE state = 'Active'"));
        Assert.Contains((await ActiveAsync()).Number, numbers);
    }

    [Fact(DisplayName = "BR-CS-003: on a database with no version the built-in version of this release is kept as the first and put in use, and keeping it again writes nothing")]
    public async Task OnNoVersionTheBuiltInIsTheFirstInUse()
    {
        SegmentBuiltInKept? kept = await KeepBuiltInAsync(Started);

        Assert.Equal((1, 1), (kept?.Added.Number, kept?.Active.Number));
        SegmentModelVersion active = await ActiveAsync();
        Assert.Equal((1, true, false, (DateTime?)Started), (active.Number, active.BuiltIn, active.ByHand, active.AdoptedAt));
        Assert.Equal(SegmentModel.BuiltIn, active.Model);
        Assert.Null(active.Evaluation);
        Assert.Null(await KeepBuiltInAsync(Started.AddDays(1)));
        Assert.Equal("1:Active:t", await StatesAndBuiltInAsync());
    }

    [Fact(DisplayName = "BR-CS-003: once the versions and what was detected with them are emptied, with no round of learning or chapters of an encode remembering a number, the built-in version is kept again as version 1 in use")]
    public async Task OnceEmptiedTheBuiltInIsVersionOneAgain()
    {
        await KeepBuiltInAsync(FirstStarted);
        await AddAsync(SegmentModelVersion.Learned(2, HandMadeModels.First, Unmeasured, FirstStarted.AddDays(1)));
        await AddAsync(SegmentModelVersion.Learned(3, HandMadeModels.First, Unmeasured, FirstStarted.AddDays(2)));
        await ExecuteAsync("INSERT INTO segment_detection (recording_id, model_version, state, segments, cm_share, snaps, extraction_version, catalog_until, detected_at) VALUES (gen_random_uuid(), 1, 'NothingFound', '[{\"startsAtSec\": 0, \"endsAtSec\": 1800, \"kind\": \"Programme\", \"probability\": 0.9}]', 0, '[]', 2, now(), now())");

        await ExecuteAsync("TRUNCATE segment_model_version CASCADE");
        SegmentBuiltInKept? kept = await KeepBuiltInAsync(Started);

        Assert.Equal(1, kept?.Added.Number);
        Assert.Equal("1:Active:t", await StatesAndBuiltInAsync());
        Assert.Equal(1, (await ActiveAsync()).Number);
    }

    [Fact(DisplayName = "BR-CS-003: an earlier built-in version in use and not chosen by hand gives way to the built-in version of this release, kept under the number after the highest; the earlier one is taken out of use and keeps what was detected with it")]
    public async Task AnEarlierBuiltInVersionGivesWay()
    {
        await AddAsync(SegmentModelVersion.Rehydrate(1, SegmentModelVersionState.Active, false, HandMadeModels.First, FirstStarted, FirstStarted, null, null, null, builtIn: true));
        await AddAsync(SegmentModelVersion.Learned(2, HandMadeModels.First, Unmeasured, FirstStarted.AddDays(1)));

        SegmentBuiltInKept? kept = await KeepBuiltInAsync(Started);

        Assert.Equal((3, 3), (kept?.Added.Number, kept?.Active.Number));
        Assert.Equal("1:Retired:t 2:Candidate:f 3:Active:t", await StatesAndBuiltInAsync());
        SegmentModelVersion active = await ActiveAsync();
        Assert.Equal((SegmentModel.BuiltIn, (DateTime?)Started), (active.Model, active.AdoptedAt));
        await using AsyncServiceScope reading = container.CreateAsyncScope();
        SegmentModelVersion left = (await Versions(reading).ListAsync(Cancel)).Single(version => version.Number == 1);
        Assert.Equal(((DateTime?)FirstStarted, (DateTime?)Started, HandMadeModels.First), (left.AdoptedAt, left.RetiredAt, left.Model));
    }

    [Theory(DisplayName = "BR-CS-003: beside a version chosen by hand or learned in use, the built-in version is kept as a candidate and the version in use stays")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BesideAVersionChosenOrLearnedTheBuiltInWaits(bool chosenByHand)
    {
        await AddAsync(chosenByHand
            ? SegmentModelVersion.Rehydrate(1, SegmentModelVersionState.Active, true, HandMadeModels.First, FirstStarted, FirstStarted, null, FirstStarted.AddDays(1), null, builtIn: true)
            : SegmentModelVersion.Rehydrate(1, SegmentModelVersionState.Active, false, HandMadeModels.First, FirstStarted, FirstStarted, null, null, null, Unmeasured));

        SegmentBuiltInKept? kept = await KeepBuiltInAsync(Started);

        Assert.Equal((2, 1), (kept?.Added.Number, kept?.Active.Number));
        Assert.Equal(chosenByHand ? "1:Active:t 2:Candidate:t" : "1:Active:f 2:Candidate:t", await StatesAndBuiltInAsync());
        Assert.Equal((1, chosenByHand), ((await ActiveAsync()).Number, (await ActiveAsync()).ByHand));
    }

    [Fact(DisplayName = "BR-CS-003: processes starting at once keep the built-in version once, and exactly one version is in use after them")]
    public async Task ProcessesStartingAtOnceKeepItOnce()
    {
        using var gate = new Barrier(4);
        SegmentBuiltInKept?[] answered = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            await using AsyncServiceScope scope = container.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<CarinaDbContext>().Database.OpenConnectionAsync(Cancel);
            gate.SignalAndWait(Cancel);

            return await Versions(scope).KeepBuiltInAsync(SegmentModel.BuiltIn, Started, Cancel);
        })));

        Assert.Single(answered, kept => kept is not null);
        Assert.Equal("1:Active:t", await StatesAndBuiltInAsync());
    }

    [Fact(DisplayName = "BR-CS-003: with the candidate of the highest number gone, a new version is numbered after the number a round of learning still remembers, never under it again")]
    public async Task TheHighestNumberGoneIsNotGivenAgain()
    {
        await KeepBuiltInAsync(FirstStarted);
        SegmentModelVersion highest = await AddCandidateMeasuredAtFirstStartAsync(Started);
        await ExecuteAsync(Round("2026-11-03", candidate: highest.Number, inUseVersion: 1));
        await ExecuteAsync($"DELETE FROM segment_model_version WHERE number = {highest.Number}");

        SegmentModelVersion next = await AddCandidateMeasuredAtFirstStartAsync(Started.AddDays(1));

        Assert.Equal((2, 3), (highest.Number, next.Number));
        Assert.Equal("1:Active 3:Candidate", await StatesAsync());
    }

    [Fact(DisplayName = "BR-CS-003: on a version table emptied while rounds of learning still remember numbers, the built-in version goes on from the highest remembered, and so does the next candidate")]
    public async Task OnAnEmptiedTableTheBuiltInGoesOnFromTheRounds()
    {
        await KeepBuiltInAsync(FirstStarted);
        await ExecuteAsync(Round("2026-10-24", candidate: 2, inUseVersion: 1));
        await ExecuteAsync(Round("2026-10-25", candidate: 4, inUseVersion: 3));
        await ExecuteAsync("TRUNCATE segment_model_version CASCADE");

        SegmentBuiltInKept? kept = await KeepBuiltInAsync(Started);
        SegmentModelVersion candidate = await AddCandidateMeasuredAtFirstStartAsync(Started.AddDays(1));

        Assert.Equal((5, 5, 6), (kept?.Added.Number, kept?.Active.Number, candidate.Number));
        Assert.Equal("5:Active 6:Candidate", await StatesAsync());
    }

    [Fact(DisplayName = "BR-CS-003: on a version table emptied while the chapters of an encode still remember a number, the built-in version goes on after it")]
    public async Task OnAnEmptiedTableTheBuiltInGoesOnFromTheChapters()
    {
        await KeepBuiltInAsync(FirstStarted);
        await ExecuteAsync(Encoded(7));
        await ExecuteAsync("TRUNCATE segment_model_version CASCADE");

        SegmentBuiltInKept? kept = await KeepBuiltInAsync(Started);

        Assert.Equal((8, 8), (kept?.Added.Number, kept?.Active.Number));
    }

    [Fact(DisplayName = "BR-CS-003: the store names the same versions as named that the rows laid name: a detection, a viewer's skip, the chapters of an encode or a round measuring the version in use, and not a round that only kept it as its candidate")]
    public async Task TheStoreNamesWhatTheRowsName()
    {
        IReadOnlyDictionary<int, bool> named = await LaidAsync();

        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IReadOnlySet<int> found = await Versions(scope).NamedAmongAsync([.. named.Keys, 99], Cancel);

        Assert.Equal(named.Where(pair => pair.Value).Select(pair => pair.Key).Order(), found.Order());
    }

    [Fact(DisplayName = "BR-CS-003: the statement that lets go of candidates deletes, among those numbered before the latest, exactly the versions the version itself says learning lets go of, the one just before the latest included and the latest kept")]
    public async Task TheStatementLetsGoOfWhatEachVersionSays()
    {
        IReadOnlyDictionary<int, bool> named = await LaidAsync();
        IReadOnlyList<SegmentModelVersion> kept;

        await using (AsyncServiceScope reading = container.CreateAsyncScope())
        {
            kept = await Versions(reading).ListAsync(Cancel);
        }

        int[] expected = [.. kept.Where(version => version.IsLetGoBefore(Latest, named[version.Number])).Select(version => version.Number).Order()];

        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IReadOnlyList<int> letGo = await Versions(scope).LetGoAsync([.. kept.Select(version => version.Number).Where(number => number < Latest)], Cancel);

        Assert.Equal(expected, letGo);
        Assert.Contains(Latest - 1, letGo);
        Assert.Equal(expected, SegmentModelVersion.LetGoBefore(kept, Latest, named.Where(pair => pair.Value).Select(pair => pair.Key).ToHashSet()));
        Assert.Equal(kept.Count - expected.Length, (await StatesAsync()).Split(' ').Length);
        Assert.Contains($"{Latest}:Candidate", await StatesAsync(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Lays the built-in version in use, a version used before, a candidate built in, and candidates learning made,
    /// each named by one of the rows that remember versions or by none, the one just before the latest and the latest
    /// named by none; answers each number and whether something names it.
    /// </summary>
    private async Task<IReadOnlyDictionary<int, bool>> LaidAsync()
    {
        await KeepBuiltInAsync(FirstStarted);

        foreach (int number in new[] { 2, 3, 4, 5, 6, 7, Latest - 1, Latest })
        {
            await AddAsync(SegmentModelVersion.Learned(number, HandMadeModels.First, MeasuredAtFirstStart, FirstStarted.AddDays(number)));
        }

        await AddAsync(SegmentModelVersion.Rehydrate(8, SegmentModelVersionState.Candidate, false, HandMadeModels.First, FirstStarted.AddDays(8), null, null, null, null, builtIn: true));
        await AddAsync(SegmentModelVersion.Rehydrate(9, SegmentModelVersionState.Retired, false, HandMadeModels.First, FirstStarted.AddDays(9), FirstStarted.AddDays(9), FirstStarted.AddDays(10), null, null, MeasuredAtFirstStart));
        await ExecuteAsync(Detected(3));
        await SkippedAsync(4);
        await ExecuteAsync(Encoded(5));
        await ExecuteAsync(Round("2026-10-20", candidate: 7, inUseVersion: 6));

        return new Dictionary<int, bool>
        {
            [1] = false,
            [2] = false,
            [3] = true,
            [4] = true,
            [5] = true,
            [6] = true,
            [7] = false,
            [8] = false,
            [9] = false,
            [Latest - 1] = false,
            [Latest] = false,
        };
    }

    private async Task<SegmentBuiltInKept?> KeepBuiltInAsync(DateTime at)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        return await Versions(scope).KeepBuiltInAsync(SegmentModel.BuiltIn, at, Cancel);
    }

    private async Task<SegmentModelVersion> ActiveAsync()
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        return await Versions(scope).ActiveAsync(Cancel);
    }

    private async Task<SegmentModelVersion> AddCandidateMeasuredAtFirstStartAsync(DateTime at)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        return await Versions(scope).AddCandidateAsync(HandMadeModels.First, MeasuredAtFirstStart, at, Cancel);
    }

    private async Task AddAsync(SegmentModelVersion version)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        CarinaDbContext context = scope.ServiceProvider.GetRequiredService<CarinaDbContext>();
        context.Add(version);
        await context.SaveChangesAsync(Cancel);
    }

    private async Task SkippedAsync(int version)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<ISkipEventRepository>().TakeAsync(
            SkipEvent.Of(RecordingId.New(), new ViewerAction(Started, ViewerActionKind.Skipped, SegmentKind.Break, TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(690), version)),
            Cancel);
    }

    private Task<string> StatesAndHandsAsync()
        => ScalarAsync("SELECT string_agg(number || ':' || state || ':' || by_hand, ' ' ORDER BY number) FROM segment_model_version");

    private Task<string> StatesAndBuiltInAsync()
        => ScalarAsync("SELECT string_agg(number || ':' || state || ':' || built_in::text::char, ' ' ORDER BY number) FROM segment_model_version");

    private Task<string> StatesAsync()
        => ScalarAsync("SELECT string_agg(number || ':' || state, ' ' ORDER BY number) FROM segment_model_version");

    private async Task ExecuteAsync(string sql)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await using System.Data.Common.DbCommand command = await CommandAsync(scope, sql);

        await command.ExecuteNonQueryAsync(Cancel);
    }

    private async Task<string> ScalarAsync(string sql)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await using System.Data.Common.DbCommand command = await CommandAsync(scope, sql);

        return Convert.ToString(await command.ExecuteScalarAsync(Cancel), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static async Task<System.Data.Common.DbCommand> CommandAsync(AsyncServiceScope scope, string sql)
    {
        CarinaDbContext context = scope.ServiceProvider.GetRequiredService<CarinaDbContext>();
        await context.Database.OpenConnectionAsync(Cancel);
        System.Data.Common.DbCommand command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;

        return command;
    }

    private static SegmentModelVersion Version(
        int number,
        SegmentModelVersionState state,
        DateTime? adoptedAt = null,
        DateTime? retiredAt = null,
        SegmentEvaluation? evaluation = null,
        DateTime? rolledBackAt = null,
        SegmentRollback? rollback = null)
        => SegmentModelVersion.Rehydrate(number, state, false, HandMadeModels.First, Noon.AddHours(-1), adoptedAt, retiredAt, null, rolledBackAt, evaluation, rollback);

    private async Task<IReadOnlyList<SegmentModelVersion>> ListAsync()
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        return await Versions(scope).ListAsync(Cancel);
    }

    private async Task<SegmentModelVersion> AddCandidateAsync(DateTime trainedAt)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        return await Versions(scope).AddCandidateAsync(HandMadeModels.First, Evaluated, trainedAt, Cancel);
    }

    private async Task<SegmentModelVersion?> ChangeAsync(SegmentVersionChange change, DateTime at)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        return await Versions(scope).ChangeAsync(change, at, Cancel);
    }

    /// <summary>The first version alone in use with the first model, as the tests of what a change does start from.</summary>
    private async Task KeepFirstInUseAsync()
        => await AddAsync(SegmentModelVersion.Rehydrate(SegmentModelVersion.FirstNumber, SegmentModelVersionState.Active, false, HandMadeModels.First, Installed, Installed, null, null, null));

    private static ISegmentModelVersionRepository Versions(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<ISegmentModelVersionRepository>();
}
