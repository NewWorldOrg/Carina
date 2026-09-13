using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class WhatALinkIntoTheNewRootWouldMeet : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_migration_standing_finding",
            table: "migration_standing");

        migrationBuilder.DropCheckConstraint(
            name: "ck_migration_standing_subject",
            table: "migration_standing");

        migrationBuilder.AddCheckConstraint(
            name: "ck_migration_standing_finding",
            table: "migration_standing",
            sql: "(subject <> 'TheNewRoot' OR finding IN ('TheNewRootIsEmpty', 'TheNewRootIsNotEmpty', 'TheNewRootIsNotThere'))\nAND (subject <> 'WhereEncodesGo' OR finding IN ('WhereEncodesGoIsSettled', 'NothingSaysWhereEncodesGo', 'MoreThanOneSaysWhereEncodesGo', 'TheProfileIsNotOffered'))\nAND (subject <> 'CarryingIntoTheNewRoot' OR finding IN ('TheCarryWouldBeAHardLink', 'TheCarryWouldCrossAMount', 'TheNewRootDoesNotTakeALink', 'NothingIsThereToCarry'))");

        migrationBuilder.AddCheckConstraint(
            name: "ck_migration_standing_subject",
            table: "migration_standing",
            sql: "subject IN ('TheNewRoot', 'WhereEncodesGo', 'CarryingIntoTheNewRoot')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_migration_standing_finding",
            table: "migration_standing");

        migrationBuilder.DropCheckConstraint(
            name: "ck_migration_standing_subject",
            table: "migration_standing");

        migrationBuilder.AddCheckConstraint(
            name: "ck_migration_standing_finding",
            table: "migration_standing",
            sql: "(subject <> 'TheNewRoot' OR finding IN ('TheNewRootIsEmpty', 'TheNewRootIsNotEmpty', 'TheNewRootIsNotThere'))\nAND (subject <> 'WhereEncodesGo' OR finding IN ('WhereEncodesGoIsSettled', 'NothingSaysWhereEncodesGo', 'MoreThanOneSaysWhereEncodesGo', 'TheProfileIsNotOffered'))");

        migrationBuilder.AddCheckConstraint(
            name: "ck_migration_standing_subject",
            table: "migration_standing",
            sql: "subject IN ('TheNewRoot', 'WhereEncodesGo')");
    }
}
