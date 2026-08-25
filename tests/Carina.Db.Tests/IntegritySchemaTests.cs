using Carina.Domain.Integrity;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class IntegritySchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private const string Began = "timestamptz '2026-08-26 05:00:00+00'";

    private const string Ended = "timestamptz '2026-08-26 05:00:03+00'";

    private const string Recording = "'00000001-0000-0000-0000-000000000002'";

    public static TheoryData<string> Faults => Named(Enum.GetValues<IntegrityFault>());

    public static TheoryData<string> NamingARecording => Named(IntegrityFaults.ThatNameARecording);

    public static TheoryData<string> WeighingTheFile => Named(IntegrityFaults.ThatWeighedTheFile);

    [Theory]
    [MemberData(nameof(Faults))]
    public async Task EveryClassTheApplicationCanNameIsOneTheTableTakes(string fault)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid check = await CheckAsync(connection);

        await FindingAsync(connection, check, Shaped(Enum.Parse<IntegrityFault>(fault)), $"'{fault}.m2ts'");

        Assert.Equal(1L, await CountAsync(connection, $"check_id = '{check}' AND fault = '{fault}'"));
    }

    [Theory]
    [MemberData(nameof(NamingARecording))]
    public async Task EveryClassThatNamesARecordingIsRefusedWithoutOne(string fault)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid check = await CheckAsync(connection);

        IntegrityFault named = Enum.Parse<IntegrityFault>(fault);
        string observed = IntegrityFaults.ThatWeighedTheFile.Contains(named) ? "99" : "NULL";

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => FindingAsync(connection, check, $"'{fault}', NULL, 100, {observed}", "'one.m2ts'"));

        Assert.Equal("ck_integrity_finding_recording", refusal.ConstraintName);
    }

    [Theory]
    [MemberData(nameof(WeighingTheFile))]
    public async Task EveryClassThatWeighedTheFileIsRefusedWithoutASize(string fault)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid check = await CheckAsync(connection);
        IntegrityFault named = Enum.Parse<IntegrityFault>(fault);
        string recording = IntegrityFaults.ThatNameARecording.Contains(named) ? Recording : "NULL";
        string ledger = IntegrityFaults.ThatNameARecording.Contains(named) ? "100" : "NULL";

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => FindingAsync(connection, check, $"'{fault}', {recording}, {ledger}, NULL", "'one.m2ts'"));

        Assert.Equal("ck_integrity_finding_observed_size", refusal.ConstraintName);
    }

    private static TheoryData<string> Named(IEnumerable<IntegrityFault> faults)
    {
        var named = new TheoryData<string>();

        foreach (IntegrityFault fault in faults)
        {
            named.Add(fault.ToString());
        }

        return named;
    }

    [Fact]
    public async Task EveryClassTheApplicationCanNameGoesIntoTheDatabaseAndComesBack()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid check = await CheckAsync(connection);

        foreach (IntegrityFault fault in Enum.GetValues<IntegrityFault>())
        {
            await FindingAsync(connection, check, Shaped(fault), $"'{fault}.m2ts'");
        }

        Assert.Equal(
            ["EmptyThoughComplete", "FileEmpty", "FileMissing", "NoLedgerRow", "SizeDisagrees"],
            await FaultsAsync(connection, check));
    }

    [Fact]
    public async Task TheDatabaseHoldsExactlyTheseChecksOnACheckAndOnAFinding()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        Assert.Equal(
            ["ck_integrity_check_counts", "ck_integrity_check_span"],
            await ConstraintsAsync(connection, "integrity_check"));
        Assert.Equal(
            [
                "ck_integrity_finding_fault",
                "ck_integrity_finding_ledger_size",
                "ck_integrity_finding_observed_size",
                "ck_integrity_finding_path",
                "ck_integrity_finding_recording",
                "ck_integrity_finding_sizes",
            ],
            await ConstraintsAsync(connection, "integrity_finding"));
    }

    [Fact]
    public async Task AFindingCarriesEverythingItWasWrittenWith()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid check = await CheckAsync(connection);

        await FindingAsync(connection, check, Shaped(IntegrityFault.SizeDisagrees), "'nested/one.m2ts'");

        await using var reading = new NpgsqlCommand(
            "SELECT fault, output_root, path, recording_id, ledger_size, observed_size, noticed_at "
            + $"FROM integrity_finding WHERE check_id = '{check}' AND path = 'nested/one.m2ts'",
            connection);
        await using NpgsqlDataReader row = await reading.ExecuteReaderAsync();

        Assert.True(await row.ReadAsync());
        Assert.Equal("SizeDisagrees", row.GetString(0));
        Assert.Equal("primary", row.GetString(1));
        Assert.Equal("nested/one.m2ts", row.GetString(2));
        Assert.Equal(new Guid("00000001-0000-0000-0000-000000000002"), row.GetGuid(3));
        Assert.Equal(100L, row.GetInt64(4));
        Assert.Equal(99L, row.GetInt64(5));
        Assert.Equal(new DateTime(2026, 8, 26, 5, 0, 0, DateTimeKind.Utc), row.GetFieldValue<DateTime>(6));
    }

    [Theory]
    [InlineData("'NoLedgerRow', " + Recording + ", NULL, 1", "ck_integrity_finding_recording")]
    [InlineData("'NoLedgerRow', NULL, 5, 1", "ck_integrity_finding_ledger_size")]
    [InlineData("'NoLedgerRow', NULL, NULL, NULL", "ck_integrity_finding_observed_size")]
    [InlineData("'FileMissing', NULL, 5, NULL", "ck_integrity_finding_recording")]
    [InlineData("'FileMissing', " + Recording + ", NULL, NULL", "ck_integrity_finding_ledger_size")]
    [InlineData("'FileMissing', " + Recording + ", 5, 1", "ck_integrity_finding_observed_size")]
    [InlineData("'SizeDisagrees', NULL, 5, 1", "ck_integrity_finding_recording")]
    [InlineData("'SizeDisagrees', " + Recording + ", NULL, 1", "ck_integrity_finding_ledger_size")]
    [InlineData("'SizeDisagrees', " + Recording + ", 5, NULL", "ck_integrity_finding_observed_size")]
    [InlineData("'FileEmpty', " + Recording + ", 5, NULL", "ck_integrity_finding_observed_size")]
    [InlineData("'EmptyThoughComplete', " + Recording + ", NULL, 0", "ck_integrity_finding_ledger_size")]
    [InlineData("'Whatever', " + Recording + ", 5, 1", "ck_integrity_finding_fault")]
    [InlineData("'SizeDisagrees', " + Recording + ", -1, 1", "ck_integrity_finding_sizes")]
    [InlineData("'SizeDisagrees', " + Recording + ", 5, -1", "ck_integrity_finding_sizes")]
    public async Task AFindingWhoseShapeDoesNotMatchItsClassIsRefusedByTheNamedCheck(string values, string named)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid check = await CheckAsync(connection);

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => FindingAsync(connection, check, values, "'one.m2ts'"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal(named, refusal.ConstraintName);
    }

    [Theory]
    [InlineData("'/one.m2ts'")]
    [InlineData("'../one.m2ts'")]
    [InlineData("'a/../../one.m2ts'")]
    [InlineData("' one.m2ts'")]
    [InlineData("'one.m2ts '")]
    [InlineData("''")]
    public async Task AFindingWhosePathLeavesTheRoomIsRefusedByTheNamedCheck(string path)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid check = await CheckAsync(connection);

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => FindingAsync(connection, check, Shaped(IntegrityFault.NoLedgerRow), path));

        Assert.Equal("ck_integrity_finding_path", refusal.ConstraintName);
    }

    [Fact]
    public async Task AFindingDeeperDownIsKept()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid check = await CheckAsync(connection);

        await FindingAsync(connection, check, Shaped(IntegrityFault.NoLedgerRow), "'a/b/c/one.m2ts'");

        Assert.Equal(1L, await CountAsync(connection, $"check_id = '{check}'"));
    }

    [Fact]
    public async Task AFindingOfNoCheckIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => FindingAsync(connection, Guid.NewGuid(), Shaped(IntegrityFault.NoLedgerRow), "'one.m2ts'"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, refusal.SqlState);
    }

    [Fact]
    public async Task FindingsGoWithTheCheckTheyBelongTo()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid check = await CheckAsync(connection);
        await FindingAsync(connection, check, Shaped(IntegrityFault.NoLedgerRow), "'one.m2ts'");

        await using (var dropping = new NpgsqlCommand(
            $"DELETE FROM integrity_check WHERE id = '{check}'",
            connection))
        {
            await dropping.ExecuteNonQueryAsync();
        }

        Assert.Equal(0L, await CountAsync(connection, $"check_id = '{check}'"));
    }

    [Fact]
    public async Task ACheckThatFinishesBeforeItStartsIsRefusedByTheNamedCheck()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => CheckAsync(connection, startedAt: Ended, finishedAt: Began));

        Assert.Equal("ck_integrity_check_span", refusal.ConstraintName);
    }

    [Fact]
    public async Task ACheckThatStartsAndFinishesInTheSameMomentIsKept()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        Guid check = await CheckAsync(connection, startedAt: Began, finishedAt: Began);

        Assert.Equal(1L, await ChecksAsync(connection, check));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task ACheckThatCountsBelowNothingIsRefusedByTheNamedCheck(int which)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        int[] counts = [0, 0, 0, 0, 0, 0, 0];
        counts[which] = -1;

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => CheckAsync(connection, counts: counts));

        Assert.Equal("ck_integrity_check_counts", refusal.ConstraintName);
    }

    private static string Shaped(IntegrityFault fault)
        => fault switch
        {
            IntegrityFault.NoLedgerRow => $"'{fault}', NULL, NULL, 1",
            IntegrityFault.FileMissing => $"'{fault}', {Recording}, 100, NULL",
            _ => $"'{fault}', {Recording}, 100, 99",
        };

    private static async Task<Guid> CheckAsync(
        NpgsqlConnection connection,
        string startedAt = Began,
        string finishedAt = Ended,
        int[]? counts = null)
    {
        var id = Guid.NewGuid();
        int[] read = counts ?? [1, 0, 2, 3, 3, 0, 0];

        await using var writing = new NpgsqlCommand(
            "INSERT INTO integrity_check (id, started_at, finished_at, roots_walked, roots_out_of_reach, "
            + "files_read, ledger_rows_read, ledger_rows_judged, ledger_rows_still_writing, "
            + "ledger_rows_in_roots_out_of_reach) VALUES "
            + $"('{id}', {startedAt}, {finishedAt}, {string.Join(", ", read)})",
            connection);
        await writing.ExecuteNonQueryAsync();

        return id;
    }

    private static async Task FindingAsync(NpgsqlConnection connection, Guid check, string values, string path)
    {
        await using var writing = new NpgsqlCommand(
            "INSERT INTO integrity_finding "
            + "(id, check_id, fault, recording_id, ledger_size, observed_size, output_root, path, noticed_at) "
            + $"VALUES ('{Guid.NewGuid()}', '{check}', {values}, 'primary', {path}, {Began})",
            connection);
        await writing.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlyList<string>> FaultsAsync(NpgsqlConnection connection, Guid check)
    {
        await using var reading = new NpgsqlCommand(
            $"SELECT fault FROM integrity_finding WHERE check_id = '{check}' ORDER BY fault",
            connection);

        return await ReadAllAsync(reading);
    }

    private static async Task<IReadOnlyList<string>> ConstraintsAsync(NpgsqlConnection connection, string table)
    {
        await using var reading = new NpgsqlCommand(
            "SELECT conname FROM pg_constraint "
            + $"WHERE conrelid = '{table}'::regclass AND contype = 'c' ORDER BY conname",
            connection);

        return await ReadAllAsync(reading);
    }

    private static async Task<IReadOnlyList<string>> ReadAllAsync(NpgsqlCommand reading)
    {
        List<string> read = [];
        await using NpgsqlDataReader rows = await reading.ExecuteReaderAsync();

        while (await rows.ReadAsync())
        {
            read.Add(rows.GetString(0));
        }

        return read;
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, string where)
    {
        await using var counting = new NpgsqlCommand(
            $"SELECT count(*) FROM integrity_finding WHERE {where}",
            connection);

        return (long)(await counting.ExecuteScalarAsync())!;
    }

    private static async Task<long> ChecksAsync(NpgsqlConnection connection, Guid check)
    {
        await using var counting = new NpgsqlCommand(
            $"SELECT count(*) FROM integrity_check WHERE id = '{check}'",
            connection);

        return (long)(await counting.ExecuteScalarAsync())!;
    }
}
