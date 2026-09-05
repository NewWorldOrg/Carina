using Carina.Domain.Channels;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class StationLogoSchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private const string Now = "timestamptz '2026-09-05 12:00:00+00'";
    private const int SomeNetworkId = 32741;
    private const int SomeLogoId = 261;
    private const int SomeServiceId = 1024;
    private const int AnotherServiceId = 1025;

    [Fact]
    public async Task ThePictureComesBackFromTheTableExactlyAsItWentIn()
    {
        await using NpgsqlConnection connection = await Cleared();
        await Logo(connection, SomeLogoId);

        await using var command = new NpgsqlCommand(
            $"SELECT picture, width, height FROM station_logo WHERE network_id = {SomeNetworkId}",
            connection);
        await using NpgsqlDataReader reading = await command.ExecuteReaderAsync();

        Assert.True(await reading.ReadAsync());
        Assert.Equal<byte[]>([0x89, 0x50, 0x4E, 0x47], (byte[])reading[0]);
        Assert.Equal(64, reading.GetInt32(1));
        Assert.Equal(36, reading.GetInt32(2));
    }

    [Fact]
    public async Task ServicesThatShareOneLogoBothNameItRatherThanKeepingACopyEach()
    {
        await using NpgsqlConnection connection = await Cleared();
        await Logo(connection, SomeLogoId);
        await Service(connection, SomeServiceId, StationLogoDeclaration.InTheCommonDataTable, SomeLogoId);
        await Service(connection, AnotherServiceId, StationLogoDeclaration.InTheCommonDataTable, SomeLogoId);

        await using var command = new NpgsqlCommand(
            $"SELECT count(*) FROM broadcast_service WHERE logo_id = {SomeLogoId}",
            connection);

        Assert.Equal(2L, (long)(await command.ExecuteScalarAsync())!);
        Assert.Equal(1L, await LogoCount(connection));
    }

    [Fact]
    public async Task AServiceThatNamesALogoWithoutSayingWhereItComesFromIsRefused()
    {
        await using NpgsqlConnection connection = await Cleared();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Service(connection, SomeServiceId, StationLogoDeclaration.NotYetRead, SomeLogoId));

        Assert.Equal("ck_broadcast_service_logo", refusal.ConstraintName);
    }

    [Fact]
    public async Task AServiceSaidToKeepItsLogoInTheCommonDataTableWithoutNamingOneIsRefused()
    {
        await using NpgsqlConnection connection = await Cleared();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Service(connection, SomeServiceId, StationLogoDeclaration.InTheCommonDataTable, null));

        Assert.Equal("ck_broadcast_service_logo", refusal.ConstraintName);
    }

    [Fact]
    public async Task AServiceThatBroadcastsNoPictureNamesNoLogoAndIsTakenAsItIs()
    {
        await using NpgsqlConnection connection = await Cleared();

        await Service(connection, SomeServiceId, StationLogoDeclaration.NoPictureIsBroadcast, null);

        await using var command = new NpgsqlCommand(
            $"SELECT logo_declaration FROM broadcast_service WHERE service_id = {SomeServiceId}",
            connection);

        Assert.Equal("NoPictureIsBroadcast", (string)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task ADeclarationTheApplicationCannotNameIsRefusedByTheTableToo()
    {
        await using NpgsqlConnection connection = await Cleared();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Execute(
                connection,
                $"""
                 INSERT INTO broadcast_service
                     (network_id, service_id, name, category, logo_declaration, discovered_at, last_seen_at)
                 VALUES ({SomeNetworkId}, {SomeServiceId}, 'Fixture', 'Television', 'Sometimes', {Now}, {Now})
                 """));

        Assert.Equal("ck_broadcast_service_logo", refusal.ConstraintName);
    }

    [Fact]
    public async Task ALogoWithNoBytesInItIsRefused()
    {
        await using NpgsqlConnection connection = await Cleared();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Logo(connection, SomeLogoId, picture: "'\\x'::bytea"));

        Assert.Equal("ck_station_logo_carries_a_picture", refusal.ConstraintName);
    }

    [Theory]
    [InlineData(LogoId.MaxValue + 1)]
    [InlineData(-1)]
    public async Task ALogoIdTheApplicationWouldRefuseIsRefusedByTheTableToo(int logoId)
    {
        await using NpgsqlConnection connection = await Cleared();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Logo(connection, logoId));

        Assert.Equal("ck_station_logo_id", refusal.ConstraintName);
    }

    [Fact]
    public async Task ALogoMeasuringNothingIsRefused()
    {
        await using NpgsqlConnection connection = await Cleared();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Logo(connection, SomeLogoId, width: 0));

        Assert.Equal("ck_station_logo_measures_something", refusal.ConstraintName);
    }

    [Fact]
    public async Task TheSameLogoCannotBeKeptTwiceForOneNetwork()
    {
        await using NpgsqlConnection connection = await Cleared();
        await Logo(connection, SomeLogoId);

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Logo(connection, SomeLogoId));

        Assert.Equal("pk_station_logo", refusal.ConstraintName);
    }

    [Fact]
    public async Task ThrowingALogoAwayLeavesTheServicesThatNamedItWhereTheyWere()
    {
        await using NpgsqlConnection connection = await Cleared();
        await Logo(connection, SomeLogoId);
        await Service(connection, SomeServiceId, StationLogoDeclaration.InTheCommonDataTable, SomeLogoId);

        await Execute(connection, $"DELETE FROM station_logo WHERE logo_id = {SomeLogoId}");

        await using var command = new NpgsqlCommand(
            $"SELECT count(*) FROM broadcast_service WHERE service_id = {SomeServiceId}",
            connection);

        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
    }

    private async Task<NpgsqlConnection> Cleared()
    {
        NpgsqlConnection connection = await database.OpenAsync();

        await Execute(connection, $"DELETE FROM broadcast_service WHERE network_id = {SomeNetworkId}");
        await Execute(connection, $"DELETE FROM station_logo WHERE network_id = {SomeNetworkId}");

        return connection;
    }

    private static async Task<long> LogoCount(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT count(*) FROM station_logo WHERE network_id = {SomeNetworkId}",
            connection);

        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static Task Logo(
        NpgsqlConnection connection,
        int logoId,
        int width = 64,
        string picture = "'\\x89504e47'::bytea")
        => Execute(
            connection,
            $"""
             INSERT INTO station_logo
                 (network_id, logo_id, logo_type, logo_version, width, height, picture, collected_at)
             VALUES ({SomeNetworkId}, {logoId}, 5, 3, {width}, 36, {picture}, {Now})
             """);

    private static Task Service(
        NpgsqlConnection connection,
        int serviceId,
        StationLogoDeclaration declaration,
        int? logoId)
    {
        string named = logoId is { } id ? $"{id}" : "NULL";

        return Execute(
            connection,
            $"""
             INSERT INTO broadcast_service
                 (network_id, service_id, name, category, logo_id, logo_declaration, discovered_at, last_seen_at)
             VALUES ({SomeNetworkId}, {serviceId}, 'Fixture', 'Television', {named}, '{declaration}', {Now}, {Now})
             """);
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync();
    }
}
