using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class SessionDisplayName : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<string>(
            name: "display_name",
            table: "auth_session",
            type: "character varying(255)",
            maxLength: 255,
            nullable: true);

        migrationBuilder.Sql("UPDATE auth_session SET display_name = subject");

        migrationBuilder.AlterColumn<string>(
            name: "display_name",
            table: "auth_session",
            type: "character varying(255)",
            maxLength: 255,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(255)",
            oldMaxLength: 255,
            oldNullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_auth_session_display_name",
            table: "auth_session",
            sql: "display_name <> ''");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_auth_session_display_name",
            table: "auth_session");

        migrationBuilder.DropColumn(
            name: "display_name",
            table: "auth_session");
    }
}
