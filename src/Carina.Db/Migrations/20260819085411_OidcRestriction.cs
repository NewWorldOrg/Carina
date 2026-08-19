using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class OidcRestriction : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<string[]>(
            name: "allowed_groups",
            table: "oidc_config",
            type: "text[]",
            nullable: false,
            defaultValueSql: "'{}'");

        migrationBuilder.AddColumn<string[]>(
            name: "allowed_hosted_domains",
            table: "oidc_config",
            type: "text[]",
            nullable: false,
            defaultValueSql: "'{}'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropColumn(name: "allowed_groups", table: "oidc_config");
        migrationBuilder.DropColumn(name: "allowed_hosted_domains", table: "oidc_config");
    }
}
