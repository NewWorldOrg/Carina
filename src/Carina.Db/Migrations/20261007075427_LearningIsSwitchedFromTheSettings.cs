using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class LearningIsSwitchedFromTheSettings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "segment_settings",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false),
                learning = table.Column<bool>(type: "boolean", nullable: false),
                learning_changed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_segment_settings", x => x.id);
                table.CheckConstraint("ck_segment_settings_single_row", "id = 1");
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "segment_settings");
    }
}
