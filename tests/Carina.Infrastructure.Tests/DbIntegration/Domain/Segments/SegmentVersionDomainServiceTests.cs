using System.Globalization;

using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;
using Carina.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using static Carina.Infrastructure.Tests.DbIntegration.Domain.Segments.RowsNamingAVersion;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Segments;

[Trait("Category", "DbIntegration")]
public sealed class SegmentVersionDomainServiceTests(MigratedRepositoryDatabase database)
    : IClassFixture<MigratedRepositoryDatabase>, IAsyncLifetime
{
    private static readonly DateTime Installed = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Started = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);

    private static readonly SegmentEvaluation Evaluated = new(
        new SegmentMetrics(0.5, 0.4, [new KindMetrics(SegmentKind.Break, 0.8)], null, 0),
        [],
        new EvaluationEvidence(12, 56, Installed));

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ServiceProvider container = database.Infrastructure();

    public Task InitializeAsync() => ExecuteAsync(Emptied);

    public async Task DisposeAsync() => await container.DisposeAsync();

    [Fact(DisplayName = "BR-CS-003: learning lets go of the candidates before its latest that nothing names; one a detection, a viewer's skip, the chapters of an encode or a round measuring the version in use names stays, and so do a version built in, the versions used and the latest, under their numbers")]
    public async Task LearningLetsGoOfTheCandidatesNothingNames()
    {
        await KeepBuiltInAsync(Installed);

        foreach (int number in new[] { 2, 3, 4, 5, 6, 9, 10 })
        {
            await AddAsync(SegmentModelVersion.Learned(number, HandMadeModels.First, Evaluated, Installed.AddDays(number)));
        }

        await AddAsync(SegmentModelVersion.Rehydrate(7, SegmentModelVersionState.Candidate, false, HandMadeModels.First, Installed.AddDays(7), null, null, null, null, builtIn: true));
        await AddAsync(SegmentModelVersion.Rehydrate(8, SegmentModelVersionState.Retired, false, HandMadeModels.First, Installed.AddDays(8), Installed.AddDays(8), Installed.AddDays(9), null, null, Evaluated));
        await ExecuteAsync(Detected(3));
        await SkippedAsync(4);
        await ExecuteAsync(Encoded(5));
        await ExecuteAsync(Round("2026-10-20", candidate: 9, inUseVersion: 6));

        IReadOnlyList<int> letGo = await LetGoBeforeAsync(10);

        Assert.Equal([2, 9], letGo);
        Assert.Equal("1:Active 3:Candidate 4:Candidate 5:Candidate 6:Candidate 7:Candidate 8:Retired 10:Candidate", await StatesAsync());
        Assert.Empty(await LetGoBeforeAsync(10));
    }

    [Fact(DisplayName = "BR-CS-003: a candidate chosen by hand is in use and not let go, nor the version it took the place of")]
    public async Task ACandidateChosenMeanwhileIsNotLetGo()
    {
        await KeepBuiltInAsync(Installed);
        await AddAsync(SegmentModelVersion.Learned(2, HandMadeModels.First, Evaluated, Installed.AddDays(1)));
        await AddAsync(SegmentModelVersion.Learned(3, HandMadeModels.First, Evaluated, Installed.AddDays(2)));

        await ChangeAsync(SegmentVersionChange.ChosenByHand(2), Started);

        Assert.Empty(await LetGoBeforeAsync(3));
        Assert.Equal("1:Retired 2:Active 3:Candidate", await StatesAsync());
    }

    [Fact(DisplayName = "BR-CS-003: after the candidates before it are let go, each next candidate goes on after the highest number given, and the numbers kept do not move")]
    public async Task TheNextCandidateGoesOnAfterThoseLetGo()
    {
        await KeepBuiltInAsync(Installed);
        await AddAsync(SegmentModelVersion.Learned(2, HandMadeModels.First, Evaluated, Installed.AddDays(1)));
        await AddAsync(SegmentModelVersion.Learned(3, HandMadeModels.First, Evaluated, Installed.AddDays(2)));

        SegmentModelVersion first = await AddCandidateAsync(Started);
        IReadOnlyList<int> firstLetGo = await LetGoBeforeAsync(first.Number);
        SegmentModelVersion second = await AddCandidateAsync(Started.AddDays(1));
        IReadOnlyList<int> secondLetGo = await LetGoBeforeAsync(second.Number);

        Assert.Equal((4, 5), (first.Number, second.Number));
        Assert.Equal([2, 3], firstLetGo);
        Assert.Equal([4], secondLetGo);
        Assert.Equal("1:Active 5:Candidate", await StatesAsync());
    }

    [Fact(DisplayName = "BR-CS-003: the built-in version kept as a candidate beside a learned version in use is numbered after every number given, and the next candidate never lets it go, while it lets go of the learned candidate before it")]
    public async Task TheBuiltInCandidateIsNeverLetGo()
    {
        await AddAsync(SegmentModelVersion.Rehydrate(1, SegmentModelVersionState.Retired, false, HandMadeModels.First, Installed, Installed, Installed.AddDays(2), null, null, builtIn: true));
        await AddAsync(SegmentModelVersion.Rehydrate(3, SegmentModelVersionState.Active, false, HandMadeModels.First, Installed.AddDays(1), Installed.AddDays(2), null, null, null, Evaluated));
        await AddAsync(SegmentModelVersion.Learned(4, HandMadeModels.First, Evaluated, Installed.AddDays(5)));
        await ExecuteAsync(Round("2026-10-04", candidate: 2, inUseVersion: 1));
        await ExecuteAsync(Round("2026-10-05", candidate: 3, inUseVersion: 1));
        await ExecuteAsync(Round("2026-10-06", candidate: 4, inUseVersion: 3));

        SegmentBuiltInKept? kept = await KeepBuiltInAsync(Started);
        SegmentModelVersion candidate = await AddCandidateAsync(Started.AddDays(1));

        Assert.Equal((5, SegmentModelVersionState.Candidate, 3), (kept?.Added.Number, kept?.Added.State, kept?.Active.Number));
        Assert.Equal(6, candidate.Number);
        Assert.Equal([4], await LetGoBeforeAsync(candidate.Number));
        Assert.Equal("1:Retired 3:Active 5:Candidate 6:Candidate", await StatesAsync());
    }

    private async Task<SegmentBuiltInKept?> KeepBuiltInAsync(DateTime at)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        return await Versions(scope).KeepBuiltInAsync(SegmentModel.BuiltIn, at, Cancel);
    }

    private async Task<SegmentModelVersion> AddCandidateAsync(DateTime at)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        return await Versions(scope).AddCandidateAsync(HandMadeModels.First, Evaluated, at, Cancel);
    }

    private async Task<IReadOnlyList<int>> LetGoBeforeAsync(int latest)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<SegmentVersionDomainService>().LetGoBeforeAsync(latest, Cancel);
    }

    private async Task ChangeAsync(SegmentVersionChange change, DateTime at)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await Versions(scope).ChangeAsync(change, at, Cancel);
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

    private async Task ExecuteAsync(string sql)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        CarinaDbContext context = scope.ServiceProvider.GetRequiredService<CarinaDbContext>();
        await context.Database.OpenConnectionAsync(Cancel);
        await using System.Data.Common.DbCommand command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;

        await command.ExecuteNonQueryAsync(Cancel);
    }

    private async Task<string> StatesAsync()
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        CarinaDbContext context = scope.ServiceProvider.GetRequiredService<CarinaDbContext>();
        await context.Database.OpenConnectionAsync(Cancel);
        await using System.Data.Common.DbCommand reading = context.Database.GetDbConnection().CreateCommand();
        reading.CommandText = "SELECT string_agg(number || ':' || state, ' ' ORDER BY number) FROM segment_model_version";

        return Convert.ToString(await reading.ExecuteScalarAsync(Cancel), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static ISegmentModelVersionRepository Versions(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<ISegmentModelVersionRepository>();
}
