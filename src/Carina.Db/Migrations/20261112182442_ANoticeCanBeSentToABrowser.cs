using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class ANoticeCanBeSentToABrowser : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_notification_destination_target",
            table: "notification_destination");

        migrationBuilder.AddColumn<string>(
            name: "endpoint_hash",
            table: "notification_destination",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ux_notification_destination_endpoint_hash",
            table: "notification_destination",
            column: "endpoint_hash",
            unique: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_notification_destination_endpoint_hash",
            table: "notification_destination",
            sql: "(target = 'Push') = (endpoint_hash IS NOT NULL)\nAND (endpoint_hash IS NULL OR endpoint_hash ~ '^[0-9a-f]{64}$')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_notification_destination_target",
            table: "notification_destination",
            sql: "target IN ('Slack', 'Discord', 'Push')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_notification_destination_endpoint_hash",
            table: "notification_destination");

        migrationBuilder.DropCheckConstraint(
            name: "ck_notification_destination_endpoint_hash",
            table: "notification_destination");

        migrationBuilder.DropCheckConstraint(
            name: "ck_notification_destination_target",
            table: "notification_destination");

        migrationBuilder.Sql(
            "DELETE FROM notification_destination WHERE target = 'Push'");

        migrationBuilder.DropColumn(
            name: "endpoint_hash",
            table: "notification_destination");

        migrationBuilder.AddCheckConstraint(
            name: "ck_notification_destination_target",
            table: "notification_destination",
            sql: "target IN ('Slack', 'Discord')");
    }
}
