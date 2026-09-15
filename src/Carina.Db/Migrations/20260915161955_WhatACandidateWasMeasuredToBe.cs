using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class WhatACandidateWasMeasuredToBe : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<double>(
            name: "score_bit_error_rate_highest",
            table: "candidate_channel",
            type: "double precision",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "score_cnr_lowest_milli_decibels",
            table: "candidate_channel",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "score_evaluated_at",
            table: "candidate_channel",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "score_lock_rate",
            table: "candidate_channel",
            type: "double precision",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "score_measured_from",
            table: "candidate_channel",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "score_measured_until",
            table: "candidate_channel",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "score_samples",
            table: "candidate_channel",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_candidate_channel_score",
            table: "candidate_channel",
            sql: "(score_evaluated_at IS NULL) = (score_lock_rate IS NULL)\nAND (score_evaluated_at IS NULL) = (score_samples IS NULL)\nAND (score_evaluated_at IS NULL) = (score_measured_from IS NULL)\nAND (score_evaluated_at IS NULL) = (score_measured_until IS NULL)\nAND (score_lock_rate > 0\n     OR (score_cnr_lowest_milli_decibels IS NULL AND score_bit_error_rate_highest IS NULL))\nAND (score_evaluated_at IS NULL\n     OR (score_samples > 0\n         AND score_lock_rate >= 0\n         AND score_lock_rate <= 1\n         AND score_measured_from < score_measured_until\n         AND score_measured_until <= score_evaluated_at))\nAND (score_bit_error_rate_highest IS NULL OR score_bit_error_rate_highest >= 0)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_candidate_channel_score",
            table: "candidate_channel");

        migrationBuilder.DropColumn(
            name: "score_bit_error_rate_highest",
            table: "candidate_channel");

        migrationBuilder.DropColumn(
            name: "score_cnr_lowest_milli_decibels",
            table: "candidate_channel");

        migrationBuilder.DropColumn(
            name: "score_evaluated_at",
            table: "candidate_channel");

        migrationBuilder.DropColumn(
            name: "score_lock_rate",
            table: "candidate_channel");

        migrationBuilder.DropColumn(
            name: "score_measured_from",
            table: "candidate_channel");

        migrationBuilder.DropColumn(
            name: "score_measured_until",
            table: "candidate_channel");

        migrationBuilder.DropColumn(
            name: "score_samples",
            table: "candidate_channel");
    }
}
