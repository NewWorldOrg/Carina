using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class TheLearningDataKeepsWhetherCaptionsAreShown : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_segment_learning_data_kind",
            table: "segment_learning_data");

        migrationBuilder.AddCheckConstraint(
            name: "ck_segment_learning_data_kind",
            table: "segment_learning_data",
            sql: "kind IN (1, 2, 3, 4, 5, 6)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_segment_learning_data_kind",
            table: "segment_learning_data");

        migrationBuilder.AddCheckConstraint(
            name: "ck_segment_learning_data_kind",
            table: "segment_learning_data",
            sql: "kind IN (1, 2, 3, 4, 5)");
    }
}
