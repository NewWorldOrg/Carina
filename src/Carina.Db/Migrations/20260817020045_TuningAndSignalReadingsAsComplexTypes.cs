using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

// Tuning and the signal readings moved from owned entities to complex types. They map to
// the same columns, so the schema this leaves behind is the one it found; what changed is
// the model the snapshot has to agree with.

/// <inheritdoc />
public partial class TuningAndSignalReadingsAsComplexTypes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
