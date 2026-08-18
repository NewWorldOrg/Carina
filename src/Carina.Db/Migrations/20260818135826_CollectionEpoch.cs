using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class CollectionEpoch : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.CreateTable(
            name: "collection_epoch",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false),
                generation = table.Column<int>(type: "integer", nullable: false),
                advanced_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_collection_epoch", x => x.id);
                table.CheckConstraint("ck_collection_epoch_single_row", "id = 1 AND generation >= 1");
            });

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable(name: "collection_epoch");
}
