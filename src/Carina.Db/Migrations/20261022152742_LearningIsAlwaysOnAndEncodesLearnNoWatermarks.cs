using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <summary>
/// Lets go of the setting that turned CM, OP and ED learning on and off, since learning now always runs, and of the
/// watermarks an encode learned of each station, since an encode now takes its chapters from the segments alone. It
/// once also put the latest version made by hand in use; the built-in version of a release is now kept and put in use
/// when the application starts. Going back brings the setting back with learning on; the watermarks come back empty.
/// </summary>
public partial class LearningIsAlwaysOnAndEncodesLearnNoWatermarks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable(name: "encode_watermark");

        migrationBuilder.DropColumn(
            name: "learning",
            table: "segment_settings");

        migrationBuilder.DropColumn(
            name: "learning_changed_at",
            table: "segment_settings");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<bool>(
            name: "learning",
            table: "segment_settings",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTime>(
            name: "learning_changed_at",
            table: "segment_settings",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.Sql(
            """
            INSERT INTO segment_settings (id, learning, learning_changed_at) VALUES (1, true, now())
            ON CONFLICT (id) DO UPDATE SET learning = true, learning_changed_at = now()
            """);

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
}
