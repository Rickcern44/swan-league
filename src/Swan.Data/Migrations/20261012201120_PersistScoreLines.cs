using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swan.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersistScoreLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PointValue",
                schema: "picks",
                table: "team_picks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "score_lines",
                schema: "scoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    Points = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Reference = table.Column<string>(type: "text", nullable: false),
                    WeekScoreId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_score_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_score_lines_week_scores_WeekScoreId",
                        column: x => x.WeekScoreId,
                        principalSchema: "scoring",
                        principalTable: "week_scores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_score_lines_WeekScoreId",
                schema: "scoring",
                table: "score_lines",
                column: "WeekScoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "score_lines",
                schema: "scoring");

            migrationBuilder.DropColumn(
                name: "PointValue",
                schema: "picks",
                table: "team_picks");
        }
    }
}
