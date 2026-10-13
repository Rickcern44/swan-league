using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swan.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleSetupSeason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing duplicates are never deleted or merged: fail loudly and let an operator decide which one stays Setup.
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF (SELECT count(*) FROM league.seasons WHERE ""State"" = 0) > 1 THEN
        RAISE EXCEPTION 'Cannot enforce one Setup season: league.seasons has more than one Setup season (State = 0). Activate, complete or remove the extras, then migrate again.';
    END IF;
END $$;");

            migrationBuilder.CreateIndex(
                name: "ux_seasons_single_setup",
                schema: "league",
                table: "seasons",
                column: "State",
                unique: true,
                filter: "\"State\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_seasons_single_setup",
                schema: "league",
                table: "seasons");
        }
    }
}
