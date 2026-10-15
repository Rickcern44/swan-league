using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swan.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRosterDraftPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RosterDraftDeadline",
                schema: "league",
                table: "seasons",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsLocked",
                schema: "picks",
                table: "rosters",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockedAt",
                schema: "picks",
                table: "rosters",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                schema: "picks",
                table: "rosters",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Existing rosters are never deleted or merged: fail loudly and let an operator fix duplicates, then migrate again.
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM picks.rosters GROUP BY ""MembershipId"", ""SeasonId"" HAVING count(*) > 1) THEN
        RAISE EXCEPTION 'Cannot enforce one roster per coach and season: picks.rosters has a membership with more than one roster in the same season.';
    END IF;
END $$;");

            migrationBuilder.CreateIndex(
                name: "ux_rosters_membership_season",
                schema: "picks",
                table: "rosters",
                columns: new[] { "MembershipId", "SeasonId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_rosters_membership_season",
                schema: "picks",
                table: "rosters");

            migrationBuilder.DropColumn(
                name: "RosterDraftDeadline",
                schema: "league",
                table: "seasons");

            migrationBuilder.DropColumn(
                name: "IsLocked",
                schema: "picks",
                table: "rosters");

            migrationBuilder.DropColumn(
                name: "LockedAt",
                schema: "picks",
                table: "rosters");

            migrationBuilder.DropColumn(
                name: "Revision",
                schema: "picks",
                table: "rosters");
        }
    }
}
