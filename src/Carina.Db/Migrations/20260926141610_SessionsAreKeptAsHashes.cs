using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class SessionsAreKeptAsHashes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.RenameColumn(
            name: "id",
            table: "auth_session",
            newName: "handle");

        migrationBuilder.Sql(
            """
            UPDATE auth_session
            SET handle = rtrim(translate(encode(sha256(convert_to(handle, 'UTF8')), 'base64'), '+/', '-_'), '=')
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql("DELETE FROM auth_session");

        migrationBuilder.RenameColumn(
            name: "handle",
            table: "auth_session",
            newName: "id");
    }
}
