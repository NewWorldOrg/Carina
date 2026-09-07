using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class QualityThresholdHistory : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "quality_threshold_change",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                threshold_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                previous_value = table.Column<double>(type: "double precision", nullable: false),
                next_value = table.Column<double>(type: "double precision", nullable: false),
                changed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                changed_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_quality_threshold_change", x => x.id);
                table.CheckConstraint("ck_quality_threshold_change_key", "threshold_key IN ('PacketsLostWarning', 'PacketsLostUnwatchable', 'PacketsLeftScrambled', 'Overflows', 'LockRate', 'CarrierToNoiseFloor', 'BitErrorRateCeiling', 'SupplySilence')");
            });

        migrationBuilder.CreateIndex(
            name: "ix_quality_threshold_change_history",
            table: "quality_threshold_change",
            columns: new[] { "threshold_key", "changed_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "quality_threshold_change");
    }
}
