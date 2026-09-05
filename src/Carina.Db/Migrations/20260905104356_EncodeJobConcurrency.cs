using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <summary>
/// The encode job row's concurrency token is PostgreSQL's own <c>xmin</c>, which every row already
/// carries, so the model changes and the schema does not.
/// </summary>
public partial class EncodeJobConcurrency : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
    }
}
