using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class HowTheQueueRunsWhenNobodyAsked : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.CreateTable(
            name: "encode_auto_run",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false),
                automatically = table.Column<bool>(type: "boolean", nullable: false),
                most_cores = table.Column<int>(type: "integer", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_encode_auto_run", x => x.id);
                table.CheckConstraint("ck_encode_auto_run_cores", "most_cores >= 1 AND most_cores <= 256");
                table.CheckConstraint("ck_encode_auto_run_single_row", "id = 1");
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable(name: "encode_auto_run");
    }
}
