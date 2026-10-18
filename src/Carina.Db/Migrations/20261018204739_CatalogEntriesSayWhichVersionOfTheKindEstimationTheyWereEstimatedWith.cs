using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class CatalogEntriesSayWhichVersionOfTheKindEstimationTheyWereEstimatedWith : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<int>(
            name: "kinds_version",
            table: "segment_catalog_entry",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddCheckConstraint(
            name: "ck_segment_catalog_entry_kinds_version",
            table: "segment_catalog_entry",
            sql: "kinds_version >= 1");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_segment_catalog_entry_kinds_version",
            table: "segment_catalog_entry");

        migrationBuilder.DropColumn(
            name: "kinds_version",
            table: "segment_catalog_entry");
    }
}
