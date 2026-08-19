using Carina.Db.Migrations;

using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Carina.Db.Tests;

public sealed class AuthenticationMigrationTests
{
    private static readonly IReadOnlyList<MigrationOperation> Up =
        new Authentication { ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL" }.UpOperations;

    private static readonly IReadOnlyList<MigrationOperation> Down =
        new Authentication { ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL" }.DownOperations;

    [Fact]
    public void AuthenticationOnlyAddsTablesAndIndexesOfItsOwn()
    {
        Assert.All(Up, operation => Assert.True(
            operation is CreateTableOperation or CreateIndexOperation,
            $"{operation.GetType().Name} changes something that was already there."));
    }

    [Fact]
    public void AuthenticationCreatesTheThreeTablesTheDomainNeeds()
    {
        Assert.Equal(
            ["auth_session", "local_account", "oidc_config"],
            Up.OfType<CreateTableOperation>().Select(table => table.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ASessionIsKeyedByTheOpaqueIdTheCookieCarries()
    {
        CreateTableOperation table = Table("auth_session");

        Assert.Equal("pk_auth_session", table.PrimaryKey!.Name);
        Assert.Equal(["id"], table.PrimaryKey.Columns);
    }

    [Fact]
    public void ASessionRowHoldsExactlyWhatTheSessionListShows()
    {
        Assert.Equal(
            ["created_at", "device_label", "id", "last_used_at", "method", "revoked_at", "subject"],
            Table("auth_session").Columns.Select(column => column.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void RevocationIsTheOneThingASessionRowMayNotHaveYet()
    {
        Assert.Equal(
            ["revoked_at"],
            Table("auth_session").Columns.Where(column => column.IsNullable).Select(column => column.Name));
    }

    [Fact]
    public void TheOpaqueIdIsStoredAtTheLengthAnIssuedOneHas()
    {
        AddColumnOperation id = Table("auth_session").Columns.Single(column => column.Name == "id");

        Assert.Equal(43, id.MaxLength);
    }

    [Fact]
    public void SessionsAreLookedUpByWhoTheyBelongToAndWhenTheyWereLastUsed()
    {
        Assert.Equal(
            ["ix_auth_session_last_used_at", "ix_auth_session_subject"],
            Up.OfType<CreateIndexOperation>().Select(index => index.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ASessionRowIsHeldToItsOwnTimelineByTheDatabase()
    {
        Assert.Equal(
            ["ck_auth_session_device_label", "ck_auth_session_method", "ck_auth_session_times"],
            Table("auth_session").CheckConstraints.Select(check => check.Name).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("local_account", "pk_local_account", "ck_local_account_single_row")]
    [InlineData("oidc_config", "pk_oidc_config", "ck_oidc_config_single_row")]
    public void TheSettingsTablesAreKeyedOnTheSingleRowTheyHold(string name, string key, string singleRow)
    {
        CreateTableOperation table = Table(name);

        Assert.Equal(key, table.PrimaryKey!.Name);
        Assert.Equal(["id"], table.PrimaryKey.Columns);
        Assert.Contains(table.CheckConstraints, check => check.Name == singleRow);
    }

    [Fact]
    public void TheLocalAccountRowHoldsAHashAndNeverAPassword()
    {
        Assert.Equal(
            ["created_at", "id", "password_changed_at", "password_hash", "username"],
            Table("local_account").Columns.Select(column => column.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AnIdentityProviderIsEitherWhollyConfiguredOrNotAtAll()
    {
        CreateTableOperation table = Table("oidc_config");

        Assert.Contains(table.CheckConstraints, check => check.Name == "ck_oidc_config_whole");
        Assert.Equal(
            ["client_id", "client_secret", "discovery_url"],
            table.Columns.Where(column => column.IsNullable).Select(column => column.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AuthenticationCanBeTakenBackOutAgainWithoutTouchingAnythingElse()
    {
        Assert.Equal(
            ["auth_session", "local_account", "oidc_config"],
            Down.Cast<DropTableOperation>().Select(table => table.Name).Order(StringComparer.Ordinal));
    }

    private static CreateTableOperation Table(string name)
        => Up.OfType<CreateTableOperation>().Single(table => table.Name == name);
}
