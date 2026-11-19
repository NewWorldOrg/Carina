using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class LearningTakesOneRoundADay : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.CreateTable(
            name: "segment_learning_round",
            columns: table => new
            {
                day = table.Column<DateOnly>(type: "date", nullable: false),
                taken_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                candidate = table.Column<int>(type: "integer", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_segment_learning_round", x => x.day);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable(
            name: "segment_learning_round");
    }
}
