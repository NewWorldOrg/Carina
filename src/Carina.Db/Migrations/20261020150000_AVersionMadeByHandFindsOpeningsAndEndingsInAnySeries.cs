using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <summary>
/// Once kept a version of four kinds made by hand that told the opening and the ending in any series and put it in use in place of the one made by hand before it. It keeps nothing now: the
/// built-in version of a release is kept when the application starts, from the parameters the release carries, and
/// a database this ran on keeps what it kept. Going back changes nothing either.
/// </summary>
public partial class AVersionMadeByHandFindsOpeningsAndEndingsInAnySeries : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => ArgumentNullException.ThrowIfNull(migrationBuilder);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => ArgumentNullException.ThrowIfNull(migrationBuilder);
}
