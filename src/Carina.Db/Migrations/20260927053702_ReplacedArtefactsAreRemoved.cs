using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations
{
    /// <inheritdoc />
    public partial class ReplacedArtefactsAreRemoved : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_encode_scratch_file_name",
                table: "encode_scratch_file");

            migrationBuilder.DropCheckConstraint(
                name: "ck_encode_scratch_file_kind",
                table: "encode_scratch_file");

            migrationBuilder.AddColumn<DateTime>(
                name: "replaced_at",
                table: "encode_job",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_encode_scratch_file_name",
                table: "encode_scratch_file",
                columns: new[] { "output_root", "file_name" },
                unique: true,
                filter: "kind <> 'ReplacedArtefact'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_encode_scratch_file_kind",
                table: "encode_scratch_file",
                sql: "kind IN ('WorkFile', 'Chapters', 'ReplacedArtefact')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_encode_job_replaced",
                table: "encode_job",
                sql: "replaced_at IS NULL OR (status = 'Completed' AND artefact_name IS NOT NULL AND replaced_at >= ended_at)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_encode_scratch_file_name",
                table: "encode_scratch_file");

            migrationBuilder.DropCheckConstraint(
                name: "ck_encode_scratch_file_kind",
                table: "encode_scratch_file");

            migrationBuilder.DropCheckConstraint(
                name: "ck_encode_job_replaced",
                table: "encode_job");

            migrationBuilder.DropColumn(
                name: "replaced_at",
                table: "encode_job");

            migrationBuilder.CreateIndex(
                name: "ux_encode_scratch_file_name",
                table: "encode_scratch_file",
                columns: new[] { "output_root", "file_name" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_encode_scratch_file_kind",
                table: "encode_scratch_file",
                sql: "kind IN ('WorkFile', 'Chapters')");
        }
    }
}
