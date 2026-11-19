using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class ChaptersOfAnArtefactTellOpeningsAndEndingsApart : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_encode_chapter_kind",
            table: "encode_chapter");

        migrationBuilder.AddCheckConstraint(
            name: "ck_encode_chapter_kind",
            table: "encode_chapter",
            sql: "kind IN ('Programme', 'Break', 'Opening', 'Ending')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_encode_chapter_kind",
            table: "encode_chapter");

        migrationBuilder.Sql("UPDATE encode_chapter SET kind = 'Break' WHERE kind IN ('Opening', 'Ending')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_encode_chapter_kind",
            table: "encode_chapter",
            sql: "kind IN ('Programme', 'Break')");
    }
}
