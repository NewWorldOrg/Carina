using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <summary>
/// Lets a chapter of an artefact be an opening or an ending beside the programme and a break. The chapters kept
/// stay as they were. Going back keeps an opening and an ending as a break, which is how they were kept before.
/// </summary>
public partial class ChaptersOfAnArtefactTellOpeningsAndEndingsApart : Migration
{
    /// <inheritdoc />
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

    /// <inheritdoc />
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
