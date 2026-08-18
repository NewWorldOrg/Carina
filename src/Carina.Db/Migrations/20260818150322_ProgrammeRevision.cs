using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class ProgrammeRevision : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.CreateSequence(name: "programme_revision");

        migrationBuilder.AddColumn<long>(
            name: "revision",
            table: "programme",
            type: "bigint",
            nullable: false,
            defaultValueSql: "nextval('programme_revision')");

        migrationBuilder.CreateIndex(
            name: "ix_programme_revision",
            table: "programme",
            column: "revision",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropIndex(name: "ix_programme_revision", table: "programme");
        migrationBuilder.DropColumn(name: "revision", table: "programme");
        migrationBuilder.DropSequence(name: "programme_revision");
    }
}
