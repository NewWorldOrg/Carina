using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class MakingTheArtefactAgainWhenAsked : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropIndex(
            name: "ux_encode_job_artefact",
            table: "encode_job");

        migrationBuilder.AddColumn<bool>(
            name: "makes_it_again",
            table: "encode_job",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTime>(
            name: "name_given_up_at",
            table: "encode_job",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ux_encode_job_artefact",
            table: "encode_job",
            columns: ["output_root", "artefact_name"],
            unique: true,
            filter: "artefact_name IS NOT NULL AND name_given_up_at IS NULL");

        migrationBuilder.AddCheckConstraint(
            name: "ck_encode_job_name_given_up",
            table: "encode_job",
            sql: "name_given_up_at IS NULL OR artefact_name IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropIndex(
            name: "ux_encode_job_artefact",
            table: "encode_job");

        migrationBuilder.DropCheckConstraint(
            name: "ck_encode_job_name_given_up",
            table: "encode_job");

        migrationBuilder.DropColumn(
            name: "makes_it_again",
            table: "encode_job");

        migrationBuilder.DropColumn(
            name: "name_given_up_at",
            table: "encode_job");

        migrationBuilder.CreateIndex(
            name: "ux_encode_job_artefact",
            table: "encode_job",
            columns: ["output_root", "artefact_name"],
            unique: true,
            filter: "artefact_name IS NOT NULL");
    }
}
