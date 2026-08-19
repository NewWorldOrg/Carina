using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class Authentication : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.CreateTable(
            name: "auth_session",
            columns: table => new
            {
                id = table.Column<string>(type: "character varying(43)", maxLength: 43, nullable: false),
                subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                last_used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                device_label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_auth_session", x => x.id);
                table.CheckConstraint("ck_auth_session_device_label", "device_label <> ''");
                table.CheckConstraint("ck_auth_session_method", "method IN ('Local', 'Oidc')");
                table.CheckConstraint(
                    "ck_auth_session_times",
                    "last_used_at >= created_at AND (revoked_at IS NULL OR revoked_at >= created_at)");
            });

        migrationBuilder.CreateTable(
            name: "local_account",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false),
                username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                password_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                password_changed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_local_account", x => x.id);
                table.CheckConstraint(
                    "ck_local_account_single_row",
                    "id = 1 AND username <> '' AND password_changed_at >= created_at");
            });

        migrationBuilder.CreateTable(
            name: "oidc_config",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false),
                discovery_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                client_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                client_secret = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_oidc_config", x => x.id);
                table.CheckConstraint("ck_oidc_config_single_row", "id = 1");
                table.CheckConstraint(
                    "ck_oidc_config_whole",
                    "(discovery_url IS NULL AND client_id IS NULL AND client_secret IS NULL)"
                    + " OR (discovery_url IS NOT NULL AND client_id IS NOT NULL AND client_secret IS NOT NULL)");
            });

        migrationBuilder.CreateIndex(
            name: "ix_auth_session_last_used_at",
            table: "auth_session",
            column: "last_used_at");

        migrationBuilder.CreateIndex(
            name: "ix_auth_session_subject",
            table: "auth_session",
            column: "subject");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable(name: "auth_session");
        migrationBuilder.DropTable(name: "local_account");
        migrationBuilder.DropTable(name: "oidc_config");
    }
}
