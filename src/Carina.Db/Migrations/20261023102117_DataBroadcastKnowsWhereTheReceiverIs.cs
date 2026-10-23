using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class DataBroadcastKnowsWhereTheReceiverIs : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "data_broadcast_settings",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false),
                postal_code = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                prefecture = table.Column<short>(type: "smallint", nullable: true),
                changed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_data_broadcast_settings", x => x.id);
                table.CheckConstraint("ck_data_broadcast_settings_postal_code", "postal_code ~ '^[0-9]{7}$'");
                table.CheckConstraint("ck_data_broadcast_settings_prefecture", "prefecture BETWEEN 0 AND 49");
                table.CheckConstraint("ck_data_broadcast_settings_single_row", "id = 1");
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "data_broadcast_settings");
    }
}
