using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <summary>
/// Lets the route of a run name the NVIDIA card as what was asked for and what ran, beside the processor and the
/// VAAPI card. Going back forgets the route of every run that named it, since the check before this one cannot hold
/// the name.
/// </summary>
public partial class TheRouteOfARunMayNameAnNvidiaCard : Migration
{
    private const string Route = """
        ((encoder_asked IS NULL) = (encoder_ran IS NULL))
        AND (encoder_asked IS NULL OR status <> 'Queued')
        AND (encoder_asked IS NULL OR encoder_asked IN ('Software', 'Vaapi', 'Nvenc'))
        AND (encoder_ran IS NULL OR encoder_ran IN ('Software', 'Vaapi', 'Nvenc'))
        AND (swerve IS NULL OR swerve IN ('TheCardIsOutOfReach', 'TheCardCannotDoThisCodec', 'TheProcessorCannotDoThisCodec'))
        AND (encoder_asked IS NULL OR ((swerve IS NULL) = (encoder_asked = encoder_ran)))
        """;

    private const string RouteBefore = """
        ((encoder_asked IS NULL) = (encoder_ran IS NULL))
        AND (encoder_asked IS NULL OR status <> 'Queued')
        AND (encoder_asked IS NULL OR encoder_asked IN ('Software', 'Vaapi'))
        AND (encoder_ran IS NULL OR encoder_ran IN ('Software', 'Vaapi'))
        AND (swerve IS NULL OR swerve IN ('TheCardIsOutOfReach', 'TheCardCannotDoThisCodec', 'TheProcessorCannotDoThisCodec'))
        AND (encoder_asked IS NULL OR ((swerve IS NULL) = (encoder_asked = encoder_ran)))
        """;

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_encode_job_route",
            table: "encode_job");

        migrationBuilder.AddCheckConstraint(
            name: "ck_encode_job_route",
            table: "encode_job",
            sql: Route);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_encode_job_route",
            table: "encode_job");

        migrationBuilder.Sql(
            "UPDATE encode_job SET encoder_asked = NULL, encoder_ran = NULL, swerve = NULL WHERE encoder_asked = 'Nvenc' OR encoder_ran = 'Nvenc'");

        migrationBuilder.AddCheckConstraint(
            name: "ck_encode_job_route",
            table: "encode_job",
            sql: RouteBefore);
    }
}
