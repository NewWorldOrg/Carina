using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class WhereTheBreaksWereJudgedToBe : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<double>(
            name: "chapters_break_share",
            table: "encode_job",
            type: "double precision",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "chapters_decided_at",
            table: "encode_job",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "chapters_detector",
            table: "encode_job",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "chapters_verdict",
            table: "encode_job",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "encode_chapter",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                job_id = table.Column<Guid>(type: "uuid", nullable: false),
                ordinal = table.Column<int>(type: "integer", nullable: false),
                starts_at = table.Column<TimeSpan>(type: "interval", nullable: false),
                ends_at = table.Column<TimeSpan>(type: "interval", nullable: false),
                kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_encode_chapter", x => x.id);
                table.CheckConstraint("ck_encode_chapter_kind", "kind IN ('Programme', 'Break')");
                table.CheckConstraint("ck_encode_chapter_ordinal", "ordinal >= 0");
                table.CheckConstraint("ck_encode_chapter_span", "starts_at >= interval '0' AND ends_at > starts_at");
                table.ForeignKey(
                    name: "fk_encode_chapter_encode_job_job_id",
                    column: x => x.job_id,
                    principalTable: "encode_job",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_encode_job_chapters",
            table: "encode_job",
            sql: "((chapters_verdict IS NULL) = (chapters_decided_at IS NULL))\n"
                + "AND ((chapters_verdict IS NULL) = (chapters_detector IS NULL))\n"
                + "AND ((chapters_verdict IS NULL) = (chapters_break_share IS NULL))\n"
                + "AND (chapters_verdict IS NULL OR status <> 'Queued')\n"
                + "AND (chapters_verdict IS NULL OR chapters_verdict IN ('NotAsked', 'Marked', 'NothingFound', 'Discarded', 'Unreadable'))\n"
                + "AND (chapters_detector IS NULL OR chapters_detector IN ('Nobody', 'Ffmpeg'))\n"
                + "AND (chapters_break_share IS NULL OR chapters_break_share BETWEEN 0 AND 1)\n"
                + "AND (chapters_decided_at IS NULL OR chapters_decided_at >= started_at)");

        migrationBuilder.CreateIndex(
            name: "ux_encode_chapter_ordinal",
            table: "encode_chapter",
            columns: ["job_id", "ordinal"],
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable(name: "encode_chapter");

        migrationBuilder.DropCheckConstraint(
            name: "ck_encode_job_chapters",
            table: "encode_job");

        migrationBuilder.DropColumn(name: "chapters_break_share", table: "encode_job");

        migrationBuilder.DropColumn(name: "chapters_decided_at", table: "encode_job");

        migrationBuilder.DropColumn(name: "chapters_detector", table: "encode_job");

        migrationBuilder.DropColumn(name: "chapters_verdict", table: "encode_job");
    }
}
