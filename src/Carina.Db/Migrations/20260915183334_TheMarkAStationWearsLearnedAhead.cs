using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class TheMarkAStationWearsLearnedAhead : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.CreateTable(
            name: "encode_watermark",
            columns: table => new
            {
                network_id = table.Column<int>(type: "integer", nullable: false),
                service_id = table.Column<int>(type: "integer", nullable: false),
                learned_from = table.Column<Guid>(type: "uuid", nullable: false),
                learned_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                pattern = table.Column<byte[]>(type: "bytea", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_encode_watermark", x => new { x.network_id, x.service_id, x.learned_from });
                table.CheckConstraint("ck_encode_watermark_pattern", "octet_length(pattern) = 16200");
                table.CheckConstraint(
                    "ck_encode_watermark_service",
                    "network_id BETWEEN 0 AND 65535 AND service_id BETWEEN 0 AND 65535");
            });

        migrationBuilder.CreateIndex(
            name: "ix_encode_watermark_learned_at",
            table: "encode_watermark",
            columns: ["network_id", "service_id", "learned_at"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable(name: "encode_watermark");
    }
}
