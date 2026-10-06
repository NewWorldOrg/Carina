using Carina.Domain.Programmes;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ProgrammeMarksColumnTests
{
    private const string ScratchDatabase = "carina_programme_marks_test";

    private const string BeforeTheMarks = "20261005131602_AChannelOnATunerIsASubjectOfItsOwn";

    private const string Airs = "timestamptz '2026-10-06 20:00:00+00'";

    private const string Ends = "timestamptz '2026-10-06 20:30:00+00'";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly int[] SymbolsThatAreNotMarks =
    [
        0x1F200,
        0x3299,
        0x2491,
        0x2492,
        0x2493,
        0x2B1B,
        0x2B24,
        0x1F17F,
        0x1F157,
        0x1F240,
        0x1F227,
        0x1F22A,
        0x1F226,
        0x1F12D,
        0x1F1A0,
        0x1F23B,
        0x65B0,
        0x518D,
    ];

    [Fact]
    public async Task TheGuideAndTheArchiveHeldBeforeTheMarksAreGivenThemFromTheNamesTheyKept()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);

        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeTheMarks, Cancel);

        await using (NpgsqlConnection connection = await OpenAsync())
        {
            await HeldAsync(connection, 1, "\U0001F21Fアニメ\U0001F211", "あらすじ");
            await HeldAsync(connection, 2, "新番組", "再会");
            await KeptAsync(connection, 3, "ドラマ", "\U0001F21E\U0001F221");
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using NpgsqlConnection reading = await OpenAsync();

        Assert.Equal(["Captioned", "New"], await MarksAsync(reading, "programme", 1));
        Assert.Empty(await MarksAsync(reading, "programme", 2));
        Assert.Equal(["Rerun", "Final"], await MarksAsync(reading, "archived_programme", 3));
    }

    [Fact]
    public async Task EverySymbolIsReadTheSameWayByTheStoreAndByTheCode()
    {
        await using NpgsqlConnection connection = await MigratedAsync();
        List<string> apart = [];
        int carried = 100;

        foreach (int point in ProgrammeMarks.Symbols.Select(symbol => symbol.CodePoint).Concat(SymbolsThatAreNotMarks))
        {
            string symbol = char.ConvertFromUtf32(point);

            foreach ((string name, string summary) in Placed(symbol))
            {
                carried++;
                await HeldAsync(connection, carried, name, summary);
                string[] stored = await MarksAsync(connection, "programme", carried);
                string[] read = [.. ProgrammeMarks.In(name, summary).Select(mark => mark.ToString())];

                if (!stored.SequenceEqual(read))
                {
                    apart.Add($"{point:x}: store {string.Join(',', stored)} code {string.Join(',', read)}");
                }
            }
        }

        Assert.True(carried > 100 + ProgrammeMarks.Symbols.Count, "the sweep wrote no programme at all");
        Assert.True(apart.Count == 0, string.Join("\n", apart));
    }

    [Fact]
    public async Task EveryMarkAtOnceIsReadInTheOrderTheTableHoldsThemByBothArms()
    {
        await using NpgsqlConnection connection = await MigratedAsync();
        string every = string.Concat(ProgrammeMarks.Symbols.Reverse().Select(symbol => symbol.Symbol));

        await HeldAsync(connection, 1, every, string.Empty);
        await KeptAsync(connection, 2, string.Empty, every);

        string[] all = [.. Enum.GetValues<ProgrammeMark>().Select(mark => mark.ToString())];

        Assert.Equal(all, await MarksAsync(connection, "programme", 1));
        Assert.Equal(all, await MarksAsync(connection, "archived_programme", 2));
        Assert.Equal(all, ProgrammeMarks.In(every, string.Empty).Select(mark => mark.ToString()));
    }

    private static IEnumerable<(string Name, string Summary)> Placed(string symbol)
    {
        yield return (symbol, string.Empty);
        yield return ($"アニメ{symbol}第1話", "あらすじ");
        yield return ("アニメ", $"{symbol}あらすじ");
        yield return ($"{symbol}{symbol}", symbol);
    }

    private static async Task HeldAsync(NpgsqlConnection connection, int carried, string name, string summary)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO programme (
                network_id, service_id, event_id, transport_stream_id, start_at, end_at,
                name, summary, is_shadow, genres, items, related, has_subtitles, audio, sounds,
                video, aspect, running, source, updated_at)
            VALUES (
                1, 1024, @event, 32736, {Airs}, {Ends},
                @name, @summary, false, '[]'::jsonb, '[]'::jsonb, '[]'::jsonb,
                false, 'Undetermined', 0, 'Undetermined', 'Undetermined', 'Undetermined', 'ScheduleBasic', {Airs})
            """;
        command.Parameters.AddWithValue("event", carried);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("summary", summary);

        await command.ExecuteNonQueryAsync(Cancel);
    }

    private static async Task KeptAsync(NpgsqlConnection connection, int carried, string name, string summary)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO archived_programme (
                network_id, service_id, event_id, start_at, end_at,
                name, summary, has_subtitles, genres, items, archived_at)
            VALUES (
                1, 1024, @event, {Airs}, {Ends},
                @name, @summary, false, '[]'::jsonb, '[]'::jsonb, {Ends})
            """;
        command.Parameters.AddWithValue("event", carried);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("summary", summary);

        await command.ExecuteNonQueryAsync(Cancel);
    }

    private static async Task<string[]> MarksAsync(NpgsqlConnection connection, string table, int carried)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT marks FROM {table} WHERE event_id = @event";
        command.Parameters.AddWithValue("event", carried);

        return (string[])(await command.ExecuteScalarAsync(Cancel))!;
    }

    private static async Task<NpgsqlConnection> MigratedAsync()
    {
        await using (CarinaDbContext context = CarinaDbContextFactory.Create(Scratch()))
        {
            await context.Database.EnsureDeletedAsync(Cancel);
            await context.Database.MigrateAsync(Cancel);
        }

        return await OpenAsync();
    }

    private static async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(Scratch());
        await connection.OpenAsync(Cancel);

        return connection;
    }

    private static string Scratch()
    {
        string? configured = Environment.GetEnvironmentVariable(CarinaDbContextFactory.ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"DbIntegration tests need {CarinaDbContextFactory.ConnectionStringVariable} pointing at the compose db service.");
        }

        return new NpgsqlConnectionStringBuilder(configured) { Database = ScratchDatabase }.ConnectionString;
    }
}
