using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class WhetherAnEncodeWasAskedForSchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    [Theory(DisplayName = "a row written without saying anything about encoding asks for one, which is what every row did before one could say otherwise")]
    [InlineData("rule")]
    [InlineData("reservation")]
    [InlineData("recording")]
    public async Task ARowThatSaysNothingAsksForAnEncode(string table)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        Assert.Equal(
            "true",
            await Scalar(
                connection,
                $"""
                SELECT column_default FROM information_schema.columns
                WHERE table_name = '{table}' AND column_name = 'encode_when_recorded'
                """));

        Assert.Equal(
            "NO",
            await Scalar(
                connection,
                $"""
                SELECT is_nullable FROM information_schema.columns
                WHERE table_name = '{table}' AND column_name = 'encode_when_recorded'
                """));
    }

    [Fact(DisplayName = "what a reservation asks about encoding reaches the recorder, because the recording copies it as it begins")]
    public async Task WhatAReservationAsksReachesTheRecorder()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        Assert.Equal(
            1L,
            await Scalar(
                connection,
                """
                SELECT count(*) FROM information_schema.columns
                WHERE table_name = 'reservation_recording_tick' AND column_name = 'encode_when_recorded'
                """));
    }

    private static async Task<object?> Scalar(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        object? read = await command.ExecuteScalarAsync();

        return read is DBNull ? null : read;
    }
}
