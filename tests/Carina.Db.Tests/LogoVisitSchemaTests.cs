using Carina.Domain.Channels;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class LogoVisitSchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private const string Now = "timestamptz '2026-09-05 12:00:00+00'";
    private const int SomeNetworkId = 32742;
    private const int SomeTransportStreamId = 32743;

    [Fact]
    public async Task AVisitIsKeptOncePerTransportRatherThanOncePerAttempt()
    {
        await using NpgsqlConnection connection = await Cleared();
        await Visit(connection, LogoVisitOutcome.Collected, Now);

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Visit(connection, LogoVisitOutcome.NothingArrived, "NULL"));

        Assert.Equal("pk_logo_visit", refusal.ConstraintName);
    }

    [Fact]
    public async Task AVisitThatSaysItCollectedSomethingHasToSayWhen()
    {
        await using NpgsqlConnection connection = await Cleared();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Visit(connection, LogoVisitOutcome.Collected, "NULL"));

        Assert.Equal("ck_logo_visit_collected", refusal.ConstraintName);
    }

    [Fact]
    public async Task AVisitThatCameBackWithNothingNeedNotSayWhenItLastCollected()
    {
        await using NpgsqlConnection connection = await Cleared();

        await Visit(connection, LogoVisitOutcome.NothingArrived, "NULL");

        await using var command = new NpgsqlCommand(
            $"SELECT outcome FROM logo_visit WHERE network_id = {SomeNetworkId}",
            connection);

        Assert.Equal("NothingArrived", (string)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task AnOutcomeTheApplicationCannotNameIsRefusedByTheTableToo()
    {
        await using NpgsqlConnection connection = await Cleared();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Execute(
                connection,
                $"""
                 INSERT INTO logo_visit
                     (network_id, transport_stream_id, outcome, last_attempted_at, last_collected_at)
                 VALUES ({SomeNetworkId}, {SomeTransportStreamId}, 'Somehow', {Now}, NULL)
                 """));

        Assert.Equal("ck_logo_visit_outcome", refusal.ConstraintName);
    }

    private async Task<NpgsqlConnection> Cleared()
    {
        NpgsqlConnection connection = await database.OpenAsync();

        await Execute(connection, $"DELETE FROM logo_visit WHERE network_id = {SomeNetworkId}");

        return connection;
    }

    private static Task Visit(NpgsqlConnection connection, LogoVisitOutcome outcome, string collectedAt)
        => Execute(
            connection,
            $"""
             INSERT INTO logo_visit
                 (network_id, transport_stream_id, outcome, last_attempted_at, last_collected_at)
             VALUES ({SomeNetworkId}, {SomeTransportStreamId}, '{outcome}', {Now}, {collectedAt})
             """);

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync();
    }
}
