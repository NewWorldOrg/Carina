using System.Globalization;
using System.Text;

using Carina.Domain.Programmes;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ProgrammeSearchFoldingTests
{
    private const string ScratchDatabase = "carina_programme_folding_test";

    public const int EveryCharacterTheStoreCanHold = 0x110000 - 2048 - 1;

    private const string At = "timestamptz '2026-08-21 12:00:00+00'";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    public static readonly (string Name, int Lowest, int Highest)[] TheCharactersTheseTextsAreWrittenWith =
    [
        ("basic latin", 0x0001, 0x007F),
        ("latin-1 supplement", 0x0080, 0x00FF),
        ("latin extended a", 0x0100, 0x017F),
        ("latin extended b", 0x0180, 0x024F),
        ("spacing modifier letters", 0x02B0, 0x02FF),
        ("combining diacritical marks", 0x0300, 0x036F),
        ("greek and coptic", 0x0370, 0x03FF),
        ("cyrillic", 0x0400, 0x04FF),
        ("general punctuation", 0x2000, 0x206F),
        ("superscripts and subscripts", 0x2070, 0x209F),
        ("currency symbols", 0x20A0, 0x20CF),
        ("letterlike symbols", 0x2100, 0x214F),
        ("number forms", 0x2150, 0x218F),
        ("enclosed alphanumerics", 0x2460, 0x24FF),
        ("cjk symbols and punctuation", 0x3000, 0x303F),
        ("hiragana", 0x3040, 0x309F),
        ("katakana", 0x30A0, 0x30FF),
        ("kanbun", 0x3190, 0x319F),
        ("katakana phonetic extensions", 0x31F0, 0x31FF),
        ("enclosed cjk letters and months", 0x3200, 0x32FF),
        ("cjk compatibility", 0x3300, 0x33FF),
        ("cjk unified ideographs extension a", 0x3400, 0x4DBF),
        ("cjk unified ideographs", 0x4E00, 0x9FFF),
        ("cjk compatibility ideographs", 0xF900, 0xFAFF),
        ("vertical forms", 0xFE10, 0xFE1F),
        ("cjk compatibility forms", 0xFE30, 0xFE4F),
        ("small form variants", 0xFE50, 0xFE6F),
        ("halfwidth and fullwidth forms", 0xFF00, 0xFFEF),
        ("kana supplement", 0x1B000, 0x1B0FF),
        ("cjk unified ideographs extension b", 0x20000, 0x2A6DF),
        ("cjk compatibility ideographs supplement", 0x2F800, 0x2FA1F),
    ];

    public static readonly int[] TheMarksTheseTextsCombineWith =
    [
        0x3099,
        0x309A,
        0xFF9E,
        0xFF9F,
        0x0300,
        0x0301,
        0x0302,
        0x0308,
        0x030A,
        0x0327,
    ];

    public static TheoryData<string, string> WhatTheseProgrammesAreCalled()
    {
        var carried = new TheoryData<string, string>();

        foreach ((string name, string summary) in Broadcast)
        {
            carried.Add(name, summary);
        }

        return carried;
    }

    private static readonly (string Name, string Summary)[] Broadcast =
    [
        ("100017時", "夜の便り"),
        ("10001７時", "夜の便り"),
        ("ﾆｭｰｽ①", "ｷﾞｮｳｻﾞ"),
        ("ニュース1", "ギョウザ"),
        ("ﾊﾟﾝ", "ｳﾞｧｲｵﾘﾝ"),
        ("が", "が"),
        ("ＡＢＣ　ＤＥＦ", "ａｂｃ"),
        ("ⅷ", "℡ ㈱"),
        ("a　b", "a b"),
        ("Ångström", "Ångström"),
        ("Å", "Å"),
        ("%_\\", "％＿＼"),
        (string.Empty, string.Empty),
        ("夏", "絶景"),
        ("末尾 ", " 先頭"),
        ("ﬃ", "ﬀ"),
        ("㌔", "㍻"),
        ("İ", "i"),
        ("ß", "SS"),
        ("ｦﾝ", "3D"),
        ("ｃｈ．１", "２nd"),
        ("가", "각"),
        ("①②③", "⒈⒉"),
        ("🎬ア", "𠮟"),
        ("ＡBｃ", "AＢc"),
        ("￾ tail", "head ﷐"),
    ];

    [Theory]
    [MemberData(nameof(WhatTheseProgrammesAreCalled))]
    public async Task TheColumnTheStoreComputesIsWhatTheCodeFoldsFromTheSameNameAndSummary(
        string name,
        string summary)
    {
        await using NpgsqlConnection connection = await MigratedAsync();
        int carried = await BroadcastAsync(connection, name, summary);

        Assert.Equal(
            ProgrammeSearchText.Searchable(name, summary),
            await SearchableAsync(connection, carried));
    }

    [Fact]
    public async Task EveryCharacterTheseTextsAreWrittenWithFoldsTheSameWayInBothPlaces()
    {
        await using NpgsqlConnection connection = await MigratedAsync();
        var apart = new List<string>();
        int swept = 0;

        foreach ((string name, int lowest, int highest) in TheCharactersTheseTextsAreWrittenWith)
        {
            int[] points = [.. Storable(lowest, highest)];
            Assert.NotEmpty(points);

            swept += points.Length;
            apart.AddRange(await ApartAsync(
                connection,
                points,
                [.. points.Select(point => ProgrammeSearchText.Folded(char.ConvertFromUtf32(point)))],
                name));
        }

        Assert.True(swept > 0, "the sweep read no character at all");
        Assert.True(apart.Count == 0, string.Join("\n", apart));
    }

    [Fact]
    public async Task EveryMarkTheseTextsCombineWithFoldsTheSameWayOnTopOfEveryCharacter()
    {
        await using NpgsqlConnection connection = await MigratedAsync();
        var apart = new List<string>();
        int swept = 0;

        foreach (int mark in TheMarksTheseTextsCombineWith)
        {
            foreach ((string name, int lowest, int highest) in Combining)
            {
                string[] pairs =
                [
                    .. Storable(lowest, highest).Select(point => char.ConvertFromUtf32(point) + char.ConvertFromUtf32(mark)),
                ];

                Assert.NotEmpty(pairs);
                swept += pairs.Length;
                apart.AddRange(await ApartAsync(
                    connection,
                    pairs,
                    [.. pairs.Select(ProgrammeSearchText.Folded)],
                    $"{name} under {mark:x4}"));
            }
        }

        Assert.True(swept > 0, "the sweep read no pair at all");
        Assert.True(apart.Count == 0, string.Join("\n", apart));
    }

    [Fact]
    public async Task WhereverTheTwoDisagreeOutsideThoseRangesOneOfThemLeftTheCharacterAlone()
    {
        await using NpgsqlConnection connection = await MigratedAsync();
        var apart = new List<string>();
        int swept = 0;

        for (int block = 0; block <= 0x10FFFF; block += 0x10000)
        {
            int[] points = [.. Storable(block, Math.Min(block + 0xFFFF, 0x10FFFF))];
            Assert.NotEmpty(points);
            swept += points.Length;

            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT to_hex(asked.point), asked.folded, lower(pg_catalog.normalize(chr(asked.point), 'NFKC'))
                FROM unnest(@points, @folded) AS asked(point, folded)
                WHERE asked.folded IS DISTINCT FROM lower(pg_catalog.normalize(chr(asked.point), 'NFKC'))
                  AND asked.folded IS DISTINCT FROM chr(asked.point)
                  AND lower(pg_catalog.normalize(chr(asked.point), 'NFKC')) IS DISTINCT FROM chr(asked.point)
                """;
            command.Parameters.AddWithValue("points", points);
            command.Parameters.AddWithValue(
                "folded",
                points.Select(point => ProgrammeSearchText.Folded(char.ConvertFromUtf32(point))).ToArray());

            await using NpgsqlDataReader reading = await command.ExecuteReaderAsync(Cancel);

            while (await reading.ReadAsync(Cancel))
            {
                apart.Add($"{reading.GetString(0)}: code {reading.GetString(1)} store {reading.GetString(2)}");
            }
        }

        Assert.Equal(EveryCharacterTheStoreCanHold, swept);
        Assert.True(apart.Count == 0, string.Join("\n", apart));
    }

    [Fact]
    public void TheCodeFoldsWhatTheRuntimeWillNotNormaliseInsteadOfThrowingOverIt()
    {
        int refused = 0;

        foreach (int point in NotEveryRuntimeNormalises())
        {
            string one = char.ConvertFromUtf32(point);

            if (!Normalises(one))
            {
                refused++;
            }

            Assert.Equal(one, ProgrammeSearchText.Folded(one));
            Assert.Equal($"ア{one}ア", ProgrammeSearchText.Folded($"ｱ{one}ｱ"));
        }

        Assert.True(refused > 0, "no character in this list is one the runtime refuses to normalise");
        Assert.Equal("\uD800", ProgrammeSearchText.Folded("\uD800"));
        Assert.Equal("ア\uDC00ア", ProgrammeSearchText.Folded("ｱ\uDC00ｱ"));
    }

    private static bool Normalises(string one)
    {
        try
        {
            one.Normalize(System.Text.NormalizationForm.FormKC);

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static readonly (string Name, int Lowest, int Highest)[] Combining =
    [
        ("basic latin", 0x0001, 0x007F),
        ("latin-1 supplement", 0x0080, 0x00FF),
        ("hiragana", 0x3040, 0x309F),
        ("katakana", 0x30A0, 0x30FF),
        ("halfwidth and fullwidth forms", 0xFF00, 0xFFEF),
    ];

    private static IEnumerable<int> Storable(int lowest, int highest)
    {
        for (int point = lowest; point <= highest; point++)
        {
            if (point is 0 || point is >= 0xD800 and <= 0xDFFF)
            {
                continue;
            }

            yield return point;
        }
    }

    private static IEnumerable<int> NotEveryRuntimeNormalises()
    {
        for (int point = 0xFDD0; point <= 0xFDEF; point++)
        {
            yield return point;
        }

        for (int plane = 0; plane <= 0x10; plane++)
        {
            yield return (plane << 16) | 0xFFFE;
            yield return (plane << 16) | 0xFFFF;
        }
    }

    private static async Task<IReadOnlyList<string>> ApartAsync(
        NpgsqlConnection connection,
        int[] points,
        string[] folded,
        string where)
        => await ApartAsync(
            connection,
            [.. points.Select(char.ConvertFromUtf32)],
            folded,
            where);

    private static async Task<IReadOnlyList<string>> ApartAsync(
        NpgsqlConnection connection,
        string[] written,
        string[] folded,
        string where)
    {
        var carried = new List<string>();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT asked.written, asked.folded, lower(pg_catalog.normalize(asked.written, 'NFKC'))
            FROM unnest(@written, @folded) AS asked(written, folded)
            WHERE asked.folded IS DISTINCT FROM lower(pg_catalog.normalize(asked.written, 'NFKC'))
            """;
        command.Parameters.AddWithValue("written", written);
        command.Parameters.AddWithValue("folded", folded);

        await using NpgsqlDataReader reading = await command.ExecuteReaderAsync(Cancel);

        while (await reading.ReadAsync(Cancel))
        {
            carried.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{where} {Spelt(reading.GetString(0))}: code {Spelt(reading.GetString(1))} store {Spelt(reading.GetString(2))}"));
        }

        return carried;
    }

    private static string Spelt(string text)
    {
        var carried = new StringBuilder();

        foreach (Rune rune in text.EnumerateRunes())
        {
            carried.Append(CultureInfo.InvariantCulture, $"{rune.Value:x4} ");
        }

        return carried.ToString().TrimEnd();
    }

    private static int taken;

    private static async Task<int> BroadcastAsync(NpgsqlConnection connection, string name, string summary)
    {
        int carried = Interlocked.Increment(ref taken);

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO programme (
                network_id, service_id, event_id, transport_stream_id,
                start_at, end_at, name, summary, is_shadow,
                genres, items, related, has_subtitles, source, updated_at)
            VALUES (
                1, 1049, @event, 1,
                {At}, {At} + interval '30 minutes', @name, @summary, false,
                '[]', '[]', '[]', false, 'ScheduleBasic', {At})
            """;
        command.Parameters.AddWithValue("event", carried);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("summary", summary);

        await command.ExecuteNonQueryAsync(Cancel);

        return carried;
    }

    private static async Task<string> SearchableAsync(NpgsqlConnection connection, int carried)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT searchable FROM programme WHERE event_id = @event";
        command.Parameters.AddWithValue("event", carried);

        return (string)(await command.ExecuteScalarAsync(Cancel))!;
    }

    private static readonly SemaphoreSlim Once = new(1, 1);

    private static bool built;

    private static async Task<NpgsqlConnection> MigratedAsync()
    {
        await Once.WaitAsync(Cancel);

        try
        {
            if (!built)
            {
                await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
                await context.Database.EnsureDeletedAsync(Cancel);
                await context.Database.MigrateAsync(Cancel);
                built = true;
            }
        }
        finally
        {
            Once.Release();
        }

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
