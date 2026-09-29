using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class TheClientSecretIsSealed : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_oidc_config_whole",
            table: "oidc_config");

        migrationBuilder.AddColumn<string>(
            name: "client_secret_sealed",
            table: "oidc_config",
            type: "text",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_oidc_config_whole",
            table: "oidc_config",
            sql: "(discovery_url IS NULL AND client_id IS NULL AND client_secret IS NULL AND client_secret_sealed IS NULL) OR (discovery_url IS NOT NULL AND client_id IS NOT NULL AND (client_secret IS NULL) <> (client_secret_sealed IS NULL))");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_oidc_config_whole",
            table: "oidc_config");

        migrationBuilder.Sql(
            "UPDATE oidc_config SET discovery_url = NULL, client_id = NULL, client_secret_sealed = NULL, "
            + "allowed_groups = '{}', allowed_hosted_domains = '{}' "
            + "WHERE client_secret IS NULL AND client_secret_sealed IS NOT NULL");

        migrationBuilder.DropColumn(
            name: "client_secret_sealed",
            table: "oidc_config");

        migrationBuilder.AddCheckConstraint(
            name: "ck_oidc_config_whole",
            table: "oidc_config",
            sql: "(discovery_url IS NULL AND client_id IS NULL AND client_secret IS NULL) OR (discovery_url IS NOT NULL AND client_id IS NOT NULL AND client_secret IS NOT NULL)");
    }
}
