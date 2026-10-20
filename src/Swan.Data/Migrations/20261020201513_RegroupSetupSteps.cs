using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swan.Data.Migrations
{
    /// <inheritdoc />
    public partial class RegroupSetupSteps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                update league.seasons
                set "SetupStep" = case "SetupStep"
                    when 0 then 0
                    when 1 then 1
                    when 2 then 1
                    when 3 then 1
                    when 4 then 1
                    when 5 then 1
                    when 6 then 3
                    when 7 then 4
                    when 8 then 5
                    when 9 then 6
                    when 10 then 7
                    else "SetupStep"
                end;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The former steps 1-5 are intentionally collapsed into Schedule, so rollback
            // restores Schedule to the earliest former step (Money).
            migrationBuilder.Sql("""
                update league.seasons
                set "SetupStep" = case "SetupStep"
                    when 0 then 0
                    when 1 then 1
                    when 2 then 3
                    when 3 then 6
                    when 4 then 7
                    when 5 then 8
                    when 6 then 9
                    when 7 then 10
                    else "SetupStep"
                end;
                """);
        }
    }
}
