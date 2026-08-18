using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class CandidateChannelObservedStream : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<int>(
            name: "observed_transport_stream_id",
            table: "candidate_channel",
            type: "integer",
            nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn(
            name: "observed_transport_stream_id",
            table: "candidate_channel");
}
