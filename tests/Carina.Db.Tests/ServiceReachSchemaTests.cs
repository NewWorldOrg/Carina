using Carina.Domain.Channels;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ServiceReachSchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private const string Updated = "timestamptz '2026-08-26 05:00:00+00'";

    public static TheoryData<int> EveryWaitTheApplicationCanName =>
    [
        .. Enumerable.Range(
            ServiceReachSettings.ShortestHoursOfSilence,
            ServiceReachSettings.LongestHoursOfSilence - ServiceReachSettings.ShortestHoursOfSilence + 1),
    ];

    public static TheoryData<int> EveryWaitTheApplicationRefuses =>
    [
        .. new[]
        {
            ServiceReachSettings.ShortestHoursOfSilence - 1,
            ServiceReachSettings.LongestHoursOfSilence + 1,
            -1,
            10_000,
        }.Distinct(),
    ];

    [Theory]
    [MemberData(nameof(EveryWaitTheApplicationCanName))]
    public async Task EveryWaitTheApplicationCanNameIsOneTheTableTakes(int hours)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await WriteAsync(connection, hours);

        Assert.Equal(hours, await ReadAsync(connection));
    }

    [Fact]
    public void TheWaitsWalkedAreTheWholeRangeTheApplicationCanName()
    {
        Assert.Equal(720, EveryWaitTheApplicationCanName.Count);
    }

    [Theory]
    [MemberData(nameof(EveryWaitTheApplicationRefuses))]
    public async Task AWaitTheApplicationWouldRefuseIsRefusedByTheTableToo(int hours)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await ClearAsync(connection);

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => InsertAsync(connection, ServiceReachSettings.TheOnlyRow, hours));

        Assert.Equal("ck_service_reach_config_hours_of_silence", refusal.ConstraintName);
    }

    [Fact]
    public async Task ASecondRowOfSettingsIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await WriteAsync(connection, ServiceReachSettings.DefaultHoursOfSilence);

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => InsertAsync(connection, ServiceReachSettings.TheOnlyRow + 1, 24));

        Assert.Equal("ck_service_reach_config_single_row", refusal.ConstraintName);
    }

    [Fact]
    public async Task TheWaitIsHeldOncePerMachineRatherThanOncePerBroadcastType()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await WriteAsync(connection, 48);

        await using var command = new NpgsqlCommand("SELECT count(*) FROM service_reach_config", connection);

        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
    }

    private static async Task ClearAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("DELETE FROM service_reach_config", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task WriteAsync(NpgsqlConnection connection, int hours)
    {
        await ClearAsync(connection);
        await InsertAsync(connection, ServiceReachSettings.TheOnlyRow, hours);
    }

    private static async Task InsertAsync(NpgsqlConnection connection, int id, int hours)
    {
        await using var command = new NpgsqlCommand(
            "INSERT INTO service_reach_config (id, hours_of_silence, updated_at)"
            + $" VALUES ({id}, {hours}, {Updated})",
            connection);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ReadAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT hours_of_silence FROM service_reach_config WHERE id = {ServiceReachSettings.TheOnlyRow}",
            connection);

        return (int)(await command.ExecuteScalarAsync())!;
    }
}
