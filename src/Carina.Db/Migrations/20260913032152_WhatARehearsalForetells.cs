using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class WhatARehearsalForetells : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "migration_standing",
            columns: table => new
            {
                run_id = table.Column<Guid>(type: "uuid", nullable: false),
                subject = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                finding = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_migration_standing", x => new { x.run_id, x.subject });
                table.CheckConstraint("ck_migration_standing_finding", "(subject <> 'TheNewRoot' OR finding IN ('TheNewRootIsEmpty', 'TheNewRootIsNotEmpty', 'TheNewRootIsNotThere'))\nAND (subject <> 'WhereEncodesGo' OR finding IN ('WhereEncodesGoIsSettled', 'NothingSaysWhereEncodesGo', 'MoreThanOneSaysWhereEncodesGo', 'TheProfileIsNotOffered'))");
                table.CheckConstraint("ck_migration_standing_subject", "subject IN ('TheNewRoot', 'WhereEncodesGo')");
                table.ForeignKey(
                    name: "fk_migration_standing_migration_run_run_id",
                    column: x => x.run_id,
                    principalTable: "migration_run",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "migration_standing");
    }
}
