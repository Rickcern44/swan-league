using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swan.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSeasonTeamUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing rows are never deleted or merged: fail loudly and let an operator fix the duplicates, then migrate again.
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM schedule.season_teams GROUP BY ""SeasonId"", ""TeamId"" HAVING count(*) > 1) THEN
        RAISE EXCEPTION 'Cannot enforce unique season teams: schedule.season_teams lists a team more than once in a season.';
    END IF;
    IF EXISTS (SELECT 1 FROM schedule.season_teams GROUP BY ""SeasonId"", ""Division"", ""FinishPosition"" HAVING count(*) > 1) THEN
        RAISE EXCEPTION 'Cannot enforce unique division finishes: schedule.season_teams has two teams with the same finish in one division of a season.';
    END IF;
END $$;");

            migrationBuilder.CreateIndex(
                name: "ux_season_teams_season_division_finish",
                schema: "schedule",
                table: "season_teams",
                columns: new[] { "SeasonId", "Division", "FinishPosition" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_season_teams_season_team",
                schema: "schedule",
                table: "season_teams",
                columns: new[] { "SeasonId", "TeamId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_season_teams_season_division_finish",
                schema: "schedule",
                table: "season_teams");

            migrationBuilder.DropIndex(
                name: "ux_season_teams_season_team",
                schema: "schedule",
                table: "season_teams");
        }
    }
}
