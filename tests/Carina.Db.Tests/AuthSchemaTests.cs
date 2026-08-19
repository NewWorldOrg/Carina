using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class AuthSchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private const string Created = "timestamptz '2026-08-19 09:00:00+00'";

    private const string Hash =
        "$argon2id$v=19$m=19456,t=2,p=1$AQIDBAUGBwgJCgsMDQ4PEA$q6uu36uuq6uuq6uuq6uuq6uuq6uuq6uuq6uuq6uuq6s";

    [Fact]
    public async Task TheSameOwnerMayBeSignedInFromTwoDevicesAtOnce()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Session(connection, "id-two-devices-one", "owner-a", "iPad Safari");
        await Session(connection, "id-two-devices-two", "owner-a", "Firefox on Linux");

        Assert.Equal(2, await Count(connection, "auth_session WHERE subject = 'owner-a'"));
    }

    [Fact]
    public async Task ASessionUsedBeforeItWasCreatedIsNotARowTheDatabaseKeeps()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Session(
                connection,
                "id-used-before-created",
                "owner-b",
                "Firefox on Linux",
                lastUsed: $"{Created} - interval '1 second'"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_auth_session_times", refusal.ConstraintName);
    }

    [Fact]
    public async Task ASessionRevokedBeforeItWasCreatedIsNotARowTheDatabaseKeeps()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Session(
                connection,
                "id-revoked-before-created",
                "owner-c",
                "Firefox on Linux",
                revoked: $"{Created} - interval '1 second'"));

        Assert.Equal("ck_auth_session_times", refusal.ConstraintName);
    }

    [Fact]
    public async Task ASessionSignedInByAWayWeDoNotOfferIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Session(connection, "id-unknown-method", "owner-d", "Firefox on Linux", method: "Forwarded"));

        Assert.Equal("ck_auth_session_method", refusal.ConstraintName);
    }

    [Fact]
    public async Task ASessionWithoutADeviceLabelWouldLeaveTheListUnreadable()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Session(connection, "id-blank-label", "owner-e", string.Empty));

        Assert.Equal("ck_auth_session_device_label", refusal.ConstraintName);
    }

    [Fact]
    public async Task ASessionStartsOutNotRevoked()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Session(connection, "id-not-revoked", "owner-f", "Firefox on Linux");

        Assert.Equal(1, await Count(connection, "auth_session WHERE subject = 'owner-f' AND revoked_at IS NULL"));
    }

    [Fact]
    public async Task ThereIsOnlyEverOneLocalAccount()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await Execute(connection, "DELETE FROM local_account");
        await Account(connection, 1, "carina");

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Account(connection, 2, "second"));

        Assert.Equal("ck_local_account_single_row", refusal.ConstraintName);
    }

    [Fact]
    public async Task ALocalAccountWhosePasswordChangedBeforeItExistedIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await Execute(connection, "DELETE FROM local_account");

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Account(connection, 1, "carina", changed: $"{Created} - interval '1 second'"));

        Assert.Equal("ck_local_account_single_row", refusal.ConstraintName);
    }

    [Fact]
    public async Task ThereIsOnlyEverOneIdentityProviderConfiguration()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await Execute(connection, "DELETE FROM oidc_config");
        await Settings(connection, 1, "NULL", "NULL", "NULL");

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Settings(connection, 2, "NULL", "NULL", "NULL"));

        Assert.Equal("ck_oidc_config_single_row", refusal.ConstraintName);
    }

    [Fact]
    public async Task AnIdentityProviderMissingItsSecretIsNotHalfSaved()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await Execute(connection, "DELETE FROM oidc_config");

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Settings(connection, 1, "'https://login.example.test/c'", "'carina'", "NULL"));

        Assert.Equal("ck_oidc_config_whole", refusal.ConstraintName);
    }

    [Fact]
    public async Task AnInstallationWithNoIdentityProviderIsAStateTheDatabaseKeeps()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await Execute(connection, "DELETE FROM oidc_config");

        await Settings(connection, 1, "NULL", "NULL", "NULL");

        Assert.Equal(1, await Count(connection, "oidc_config WHERE discovery_url IS NULL"));
    }

    private static Task Session(
        NpgsqlConnection connection,
        string id,
        string subject,
        string deviceLabel,
        string method = "Local",
        string? lastUsed = null,
        string? revoked = null)
        => Execute(
            connection,
            "INSERT INTO auth_session (id, subject, method, created_at, last_used_at, device_label, revoked_at) "
            + $"VALUES ('{id}', '{subject}', '{method}', {Created}, {lastUsed ?? Created}, '{deviceLabel}', {revoked ?? "NULL"})");

    private static Task Account(
        NpgsqlConnection connection,
        int id,
        string username,
        string? changed = null)
        => Execute(
            connection,
            "INSERT INTO local_account (id, username, password_hash, created_at, password_changed_at) "
            + $"VALUES ({id}, '{username}', '{Hash}', {Created}, {changed ?? Created})");

    [Fact]
    public async Task AProviderThatNamesNobodyStillHasTwoEmptyListsRatherThanNulls()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Execute(connection, "DELETE FROM oidc_config");
        await Settings(connection, 1, "'https://login.example.test/c'", "'carina'", "'a-secret'");

        Assert.Equal(
            1,
            await Count(
                connection,
                "oidc_config WHERE allowed_groups = '{}' AND allowed_hosted_domains = '{}'"));
    }

    [Fact]
    public async Task WhoIsAllowedThroughIsKeptAsAListRatherThanOneRunTogetherString()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Execute(connection, "DELETE FROM oidc_config");
        await Settings(connection, 1, "'https://login.example.test/c'", "'carina'", "'a-secret'");
        await Execute(
            connection,
            "UPDATE oidc_config SET allowed_groups = ARRAY['operators', 'owners'] WHERE id = 1");

        Assert.Equal(
            1,
            await Count(connection, "oidc_config WHERE 'owners' = ANY(allowed_groups)"));
        Assert.Equal(
            1,
            await Count(connection, "oidc_config WHERE array_length(allowed_groups, 1) = 2"));
    }

    private static Task Settings(
        NpgsqlConnection connection,
        int id,
        string discoveryUrl,
        string clientId,
        string clientSecret)
        => Execute(
            connection,
            "INSERT INTO oidc_config (id, discovery_url, client_id, client_secret, updated_at) "
            + $"VALUES ({id}, {discoveryUrl}, {clientId}, {clientSecret}, {Created})");

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> Count(NpgsqlConnection connection, string from)
    {
        await using var command = new NpgsqlCommand($"SELECT COUNT(*) FROM {from}", connection);

        return (int)(long)(await command.ExecuteScalarAsync())!;
    }
}
