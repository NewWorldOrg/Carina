using System.Globalization;

using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class BuiltInVersionTests : IAsyncLifetime
{
    private const string ScratchDatabase = "carina_built_in_version_test";

    private static readonly DateTime Installed = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Started = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);

    private static readonly SegmentEvaluation Evaluated = new(null, [], new EvaluationEvidence(12, 148, Installed));

    private static readonly CancellationToken Cancel = CancellationToken.None;

    public async Task InitializeAsync()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
        await context.GetService<IMigrator>().MigrateAsync(cancellationToken: Cancel);
    }

    public async Task DisposeAsync()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
    }

    [Fact(DisplayName = "BR-CS-003: on a database with no version the built-in version of this release is kept as the first and put in use, and keeping it again writes nothing")]
    public async Task OnNoVersionTheBuiltInIsTheFirstInUse()
    {
        SegmentBuiltInKept? kept = await KeepAsync(Started);

        Assert.Equal((1, 1), (kept?.Added.Number, kept?.Active.Number));
        SegmentModelVersion active = await ActiveAsync();
        Assert.Equal((1, true, false, (DateTime?)Started), (active.Number, active.BuiltIn, active.ByHand, active.AdoptedAt));
        Assert.Equal(SegmentModel.BuiltIn, active.Model);
        Assert.Null(active.Evaluation);
        Assert.Null(await KeepAsync(Started.AddDays(1)));
        Assert.Equal("1:Active:t", await StatesAsync());
    }

    [Fact(DisplayName = "BR-CS-003: once the versions and what was detected with them are emptied, the built-in version is kept again as version 1 in use, whatever numbers were given before")]
    public async Task OnceEmptiedTheBuiltInIsVersionOneAgain()
    {
        await KeepAsync(Installed);
        await AddAsync(SegmentModelVersion.Learned(2, HandMadeModels.First, Evaluated, Installed.AddDays(1)));
        await AddAsync(SegmentModelVersion.Learned(3, HandMadeModels.First, Evaluated, Installed.AddDays(2)));
        await ExecuteAsync("INSERT INTO segment_detection (recording_id, model_version, state, segments, cm_share, snaps, extraction_version, catalog_until, detected_at) VALUES (gen_random_uuid(), 1, 'NothingFound', '[{\"startsAtSec\": 0, \"endsAtSec\": 1800, \"kind\": \"Programme\", \"probability\": 0.9}]', 0, '[]', 2, now(), now())");

        await ExecuteAsync("TRUNCATE segment_model_version CASCADE");
        SegmentBuiltInKept? kept = await KeepAsync(Started);

        Assert.Equal(1, kept?.Added.Number);
        Assert.Equal("1:Active:t", await StatesAsync());
        Assert.Equal(1, (await ActiveAsync()).Number);
    }

    [Fact(DisplayName = "BR-CS-003: an earlier built-in version in use and not chosen by hand gives way to the built-in version of this release, kept under the number after the highest; the earlier one is taken out of use and keeps what was detected with it")]
    public async Task AnEarlierBuiltInVersionGivesWay()
    {
        await AddAsync(SegmentModelVersion.Rehydrate(1, SegmentModelVersionState.Active, false, HandMadeModels.First, Installed, Installed, null, null, null, builtIn: true));
        await AddAsync(SegmentModelVersion.Learned(2, HandMadeModels.First, Evaluated, Installed.AddDays(1)));

        SegmentBuiltInKept? kept = await KeepAsync(Started);

        Assert.Equal((3, 3), (kept?.Added.Number, kept?.Active.Number));
        Assert.Equal("1:Retired:t 2:Candidate:f 3:Active:t", await StatesAsync());
        SegmentModelVersion active = await ActiveAsync();
        Assert.Equal((SegmentModel.BuiltIn, (DateTime?)Started), (active.Model, active.AdoptedAt));
        await using CarinaDbContext reading = CarinaDbContextFactory.Create(Scratch());
        SegmentModelVersion left = (await new SegmentModelVersionRepository(reading).ListAsync(Cancel)).Single(version => version.Number == 1);
        Assert.Equal(((DateTime?)Installed, (DateTime?)Started, HandMadeModels.First), (left.AdoptedAt, left.RetiredAt, left.Model));
    }

    [Theory(DisplayName = "BR-CS-003: beside a version chosen by hand or learned in use, the built-in version is kept as a candidate and the version in use stays")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BesideAVersionChosenOrLearnedTheBuiltInWaits(bool chosenByHand)
    {
        await AddAsync(chosenByHand
            ? SegmentModelVersion.Rehydrate(1, SegmentModelVersionState.Active, true, HandMadeModels.First, Installed, Installed, null, Installed.AddDays(1), null, builtIn: true)
            : SegmentModelVersion.Rehydrate(1, SegmentModelVersionState.Active, false, HandMadeModels.First, Installed, Installed, null, null, null, Evaluated));

        SegmentBuiltInKept? kept = await KeepAsync(Started);

        Assert.Equal((2, 1), (kept?.Added.Number, kept?.Active.Number));
        Assert.Equal(chosenByHand ? "1:Active:t 2:Candidate:t" : "1:Active:f 2:Candidate:t", await StatesAsync());
        Assert.Equal((1, chosenByHand), ((await ActiveAsync()).Number, (await ActiveAsync()).ByHand));
    }

    [Fact(DisplayName = "BR-CS-003: processes starting at once keep the built-in version once, and exactly one version is in use after them")]
    public async Task ProcessesStartingAtOnceKeepItOnce()
    {
        using var gate = new Barrier(4);
        SegmentBuiltInKept?[] answered = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            await using CarinaDbContext writing = CarinaDbContextFactory.Create(Scratch());
            await writing.Database.OpenConnectionAsync(Cancel);
            gate.SignalAndWait(Cancel);

            return await new SegmentModelVersionRepository(writing).KeepBuiltInAsync(SegmentModel.BuiltIn, Started, Cancel);
        })));

        Assert.Single(answered, kept => kept is not null);
        Assert.Equal("1:Active:t", await StatesAsync());
    }

    private static async Task<SegmentBuiltInKept?> KeepAsync(DateTime at)
    {
        await using CarinaDbContext writing = CarinaDbContextFactory.Create(Scratch());

        return await new SegmentModelVersionRepository(writing).KeepBuiltInAsync(SegmentModel.BuiltIn, at, Cancel);
    }

    private static async Task<SegmentModelVersion> ActiveAsync()
    {
        await using CarinaDbContext reading = CarinaDbContextFactory.Create(Scratch());

        return await new SegmentModelVersionRepository(reading).ActiveAsync(Cancel);
    }

    private static async Task AddAsync(SegmentModelVersion version)
    {
        await using CarinaDbContext writing = CarinaDbContextFactory.Create(Scratch());
        writing.Add(version);
        await writing.SaveChangesAsync(Cancel);
    }

    private static Task<string> StatesAsync()
        => ScalarAsync("SELECT string_agg(number || ':' || state || ':' || built_in::text::char, ' ' ORDER BY number) FROM segment_model_version");

    private static string Scratch()
    {
        string connection = Environment.GetEnvironmentVariable(CarinaDbContextFactory.ConnectionStringVariable)
                            ?? throw new InvalidOperationException($"{CarinaDbContextFactory.ConnectionStringVariable} names the database the tests make theirs beside.");

        return new NpgsqlConnectionStringBuilder(connection) { Database = ScratchDatabase }.ConnectionString;
    }

    private static async Task ExecuteAsync(string sql)
    {
        await using NpgsqlConnection connection = new(Scratch());
        await connection.OpenAsync(Cancel);
        await using NpgsqlCommand command = new(sql, connection);

        await command.ExecuteNonQueryAsync(Cancel);
    }

    private static async Task<string> ScalarAsync(string sql)
    {
        await using NpgsqlConnection connection = new(Scratch());
        await connection.OpenAsync(Cancel);
        await using NpgsqlCommand reading = new(sql, connection);

        return Convert.ToString(await reading.ExecuteScalarAsync(Cancel), CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
