using Npgsql;

namespace Carina.Db.Tests.DbIntegration.Infra.Rules;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class RulesSchemaTests(MigratedScratchDatabase database) : IClassFixture<MigratedScratchDatabase>
{
    [Fact(DisplayName = "a row written without saying anything about encoding asks for one, which is what every row did before one could say otherwise")]
    public async Task ARowThatSaysNothingAsksForAnEncode()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        Assert.Equal(
            "true",
            await Scalar(
                connection,
                """
                SELECT column_default FROM information_schema.columns
                WHERE table_name = 'rule' AND column_name = 'encode_when_recorded'
                """));

        Assert.Equal(
            "NO",
            await Scalar(
                connection,
                """
                SELECT is_nullable FROM information_schema.columns
                WHERE table_name = 'rule' AND column_name = 'encode_when_recorded'
                """));
    }

    private static async Task<object?> Scalar(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        object? read = await command.ExecuteScalarAsync();

        return read is DBNull ? null : read;
    }
}
