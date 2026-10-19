using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swan.Data.Migrations;

public partial class AddNflStandingsSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "integrations");
        migrationBuilder.CreateTable(
            name: "nfl_standings_snapshots",
            schema: "integrations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Season = table.Column<int>(type: "integer", nullable: false),
                Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                DataJson = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_nfl_standings_snapshots", x => x.Id));
        migrationBuilder.CreateIndex(name: "ux_nfl_standings_snapshots_season", schema: "integrations", table: "nfl_standings_snapshots", column: "Season", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "nfl_standings_snapshots", schema: "integrations");
}
