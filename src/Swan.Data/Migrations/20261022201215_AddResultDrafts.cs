using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swan.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddResultDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProposedResult",
                schema: "schedule",
                table: "games",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProposedResultReason",
                schema: "schedule",
                table: "games",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProposedResultSource",
                schema: "schedule",
                table: "games",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProposedResultUpdatedAt",
                schema: "schedule",
                table: "games",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProposedResult",
                schema: "schedule",
                table: "games");

            migrationBuilder.DropColumn(
                name: "ProposedResultReason",
                schema: "schedule",
                table: "games");

            migrationBuilder.DropColumn(
                name: "ProposedResultSource",
                schema: "schedule",
                table: "games");

            migrationBuilder.DropColumn(
                name: "ProposedResultUpdatedAt",
                schema: "schedule",
                table: "games");
        }
    }
}
