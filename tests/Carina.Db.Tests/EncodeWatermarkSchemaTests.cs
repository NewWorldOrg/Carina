using Carina.Domain.Encodings;

using Npgsql;

using NpgsqlTypes;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class EncodeWatermarkSchemaTests(MigratedScratchDatabase database) : IClassFixture<MigratedScratchDatabase>
{
    private static readonly byte[] APattern = WatermarkMask.Covering([0, 1, 2]).Packed();

    [Fact(DisplayName = "a watermark names the recording it was learned from by value, so throwing the recording away never has to wait on it")]
    public async Task AWatermarkNamesItsRecordingByValue()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await KeepAsync(connection, 1040, Guid.NewGuid(), APattern);
    }

    [Fact(DisplayName = "one recording teaches a service one watermark")]
    public async Task OneRecordingTeachesAServiceOneWatermark()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid from = Guid.NewGuid();
        await KeepAsync(connection, 1041, from, APattern);

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => KeepAsync(connection, 1041, from, APattern));

        Assert.Equal("pk_encode_watermark", refusal.ConstraintName);
    }

    [Fact(DisplayName = "a pattern of any other length than a watermark takes is refused")]
    public async Task APatternOfAnyOtherLengthIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => KeepAsync(connection, 1042, Guid.NewGuid(), new byte[WatermarkMask.PackedBytes - 1]));

        Assert.Equal("ck_encode_watermark_pattern", refusal.ConstraintName);
    }

    [Fact(DisplayName = "a service no broadcast can carry is refused")]
    public async Task AServiceNoBroadcastCanCarryIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => KeepAsync(connection, 65536, Guid.NewGuid(), APattern));

        Assert.Equal("ck_encode_watermark_service", refusal.ConstraintName);
    }

    private static async Task KeepAsync(NpgsqlConnection connection, int service, Guid from, byte[] pattern)
    {
        await using var keeping = new NpgsqlCommand(
            """
            INSERT INTO encode_watermark (network_id, service_id, learned_from, learned_at, pattern)
            VALUES (32741, @service, @from, timestamptz '2026-09-05 04:00:00+00', @pattern)
            """,
            connection);
        keeping.Parameters.AddWithValue("service", service);
        keeping.Parameters.AddWithValue("from", from);
        keeping.Parameters.AddWithValue("pattern", NpgsqlDbType.Bytea, pattern);

        await keeping.ExecuteNonQueryAsync();
    }
}
