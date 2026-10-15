using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swan.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCoachBallotSaving : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "PointValue",
                schema: "picks",
                table: "team_picks",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<Guid[]>(
                name: "Teams",
                schema: "picks",
                table: "rosters",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                schema: "picks",
                table: "ballots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Existing ballots are never deleted or merged: fail loudly and let an operator fix duplicates, then migrate again.
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM picks.ballots GROUP BY ""MembershipId"", ""WeekId"" HAVING count(*) > 1) THEN
        RAISE EXCEPTION 'Cannot enforce one ballot per coach and week: picks.ballots has a membership with more than one ballot for the same week.';
    END IF;
END $$;");

            migrationBuilder.CreateIndex(
                name: "ux_ballots_membership_week",
                schema: "picks",
                table: "ballots",
                columns: new[] { "MembershipId", "WeekId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_ballots_membership_week",
                schema: "picks",
                table: "ballots");

            migrationBuilder.DropColumn(
                name: "Teams",
                schema: "picks",
                table: "rosters");

            migrationBuilder.DropColumn(
                name: "Revision",
                schema: "picks",
                table: "ballots");

            migrationBuilder.AlterColumn<int>(
                name: "PointValue",
                schema: "picks",
                table: "team_picks",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);
        }
    }
}
