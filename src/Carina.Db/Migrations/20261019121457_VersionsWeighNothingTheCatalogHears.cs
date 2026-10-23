using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <summary>
/// Takes the catalog's features out of every version kept, weights and scales alike, since the catalog now fixes the
/// segments it hears when the sequence is decided rather than being weighed. It once also kept a version of four kinds
/// made by hand without them; the built-in version of a release is now kept when the application starts. Going back
/// gives the versions made by hand of two and of four kinds their weights of the catalog again; a learned version stays
/// without them.
/// </summary>
public partial class VersionsWeighNothingTheCatalogHears : Migration
{
    private const string TakeTurn = "SELECT pg_advisory_xact_lock(5243197610103)";

    private const string CatalogFeatures =
        "'{catalogShare,catalogCmShare,catalogProgrammeShare,catalogOpeningShare,catalogEndingShare,catalogOccurrences,catalogSeries,catalogStations,catalogCmMultipleOf15s}'::text[]";

    private const string TwoKinds = """{"classes": {"Break": {"bias": -2.5, "features": {"isMultipleOf15s": 2.0, "edgeSilence": 1.5, "logoShare": -3.0}}}}""";

    private const string TwoKindsScales = """{"isMultipleOf15s": {"mean": 0, "sd": 1}, "edgeSilence": {"mean": 0, "sd": 1}, "logoShare": {"mean": 0, "sd": 1}}""";

    private const string FourKinds =
        """{"classes": {"Break": {"bias": -2.5, "features": {"isMultipleOf15s": 2.0, "edgeSilence": 1.5, "logoShare": -3.0}}, "Opening": {"bias": -6.0, "features": {"pastOpeningShare": 4.0}}, "Ending": {"bias": -6.0, "features": {"pastEndingShare": 4.0}}}}""";

    private const string FourKindsScales =
        """{"isMultipleOf15s": {"mean": 0, "sd": 1}, "edgeSilence": {"mean": 0, "sd": 1}, "logoShare": {"mean": 0, "sd": 1}, "pastOpeningShare": {"mean": 0, "sd": 1}, "pastEndingShare": {"mean": 0, "sd": 1}}""";

    private const string TwoKindsWithTheCatalog =
        """{"classes": {"Break": {"bias": -2.5, "features": {"isMultipleOf15s": 2.0, "edgeSilence": 1.5, "logoShare": -3.0, "catalogCmShare": 4.0}}}}""";

    private const string TwoKindsScalesWithTheCatalog =
        """{"isMultipleOf15s": {"mean": 0, "sd": 1}, "edgeSilence": {"mean": 0, "sd": 1}, "logoShare": {"mean": 0, "sd": 1}, "catalogCmShare": {"mean": 0, "sd": 1}}""";

    private const string FourKindsWithTheCatalog =
        """{"classes": {"Break": {"bias": -2.5, "features": {"isMultipleOf15s": 2.0, "edgeSilence": 1.5, "logoShare": -3.0, "catalogCmShare": 4.0}}, "Opening": {"bias": -6.0, "features": {"catalogOpeningShare": 8.0, "pastOpeningShare": 4.0}}, "Ending": {"bias": -6.0, "features": {"catalogEndingShare": 8.0, "pastEndingShare": 4.0}}}}""";

    private const string FourKindsScalesWithTheCatalog =
        """{"isMultipleOf15s": {"mean": 0, "sd": 1}, "edgeSilence": {"mean": 0, "sd": 1}, "logoShare": {"mean": 0, "sd": 1}, "catalogCmShare": {"mean": 0, "sd": 1}, "catalogOpeningShare": {"mean": 0, "sd": 1}, "pastOpeningShare": {"mean": 0, "sd": 1}, "catalogEndingShare": {"mean": 0, "sd": 1}, "pastEndingShare": {"mean": 0, "sd": 1}}""";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql(TakeTurn);
        migrationBuilder.Sql(
            $$"""
            UPDATE segment_model_version
            SET weights = jsonb_build_object('classes', (
                    SELECT coalesce(jsonb_object_agg(kind, jsonb_set(weighed, '{features}', (weighed -> 'features') - {{CatalogFeatures}})), '{}'::jsonb)
                    FROM jsonb_each(weights -> 'classes') AS classes(kind, weighed))),
                standardisation = standardisation - {{CatalogFeatures}}
            WHERE standardisation ?| {{CatalogFeatures}}
               OR EXISTS (SELECT 1 FROM jsonb_each(weights -> 'classes') AS classes(kind, weighed) WHERE (weighed -> 'features') ?| {{CatalogFeatures}})
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql(TakeTurn);
        migrationBuilder.Sql(
            $"""
            UPDATE segment_model_version
            SET weights = '{TwoKindsWithTheCatalog}', standardisation = '{TwoKindsScalesWithTheCatalog}'
            WHERE evidence IS NULL AND weights = '{TwoKinds}'::jsonb AND standardisation = '{TwoKindsScales}'::jsonb
            """);
        migrationBuilder.Sql(
            $"""
            UPDATE segment_model_version
            SET weights = '{FourKindsWithTheCatalog}', standardisation = '{FourKindsScalesWithTheCatalog}'
            WHERE evidence IS NULL AND weights = '{FourKinds}'::jsonb AND standardisation = '{FourKindsScales}'::jsonb
            """);
    }
}
