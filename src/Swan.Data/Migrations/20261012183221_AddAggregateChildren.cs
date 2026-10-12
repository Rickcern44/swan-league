using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swan.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAggregateChildren : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dog_options",
                schema: "schedule",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    WeekId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dog_options", x => x.Id);
                    table.ForeignKey(
                        name: "FK_dog_options_weeks_WeekId",
                        column: x => x.WeekId,
                        principalSchema: "schedule",
                        principalTable: "weeks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dog_picks",
                schema: "picks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    LockTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    BallotId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dog_picks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_dog_picks_ballots_BallotId",
                        column: x => x.BallotId,
                        principalSchema: "picks",
                        principalTable: "ballots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "games",
                schema: "schedule",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HomeTeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    AwayTeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kickoff = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Excluded = table.Column<bool>(type: "boolean", nullable: false),
                    ApprovedLine = table.Column<decimal>(type: "numeric", nullable: true),
                    Result = table.Column<string>(type: "text", nullable: true),
                    WeekId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_games", x => x.Id);
                    table.ForeignKey(
                        name: "FK_games_weeks_WeekId",
                        column: x => x.WeekId,
                        principalSchema: "schedule",
                        principalTable: "weeks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pick_changes",
                schema: "picks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    BallotId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pick_changes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pick_changes_ballots_BallotId",
                        column: x => x.BallotId,
                        principalSchema: "picks",
                        principalTable: "ballots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "team_picks",
                schema: "picks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    LockTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsBye = table.Column<bool>(type: "boolean", nullable: false),
                    BallotId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_picks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_team_picks_ballots_BallotId",
                        column: x => x.BallotId,
                        principalSchema: "picks",
                        principalTable: "ballots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_dog_options_WeekId",
                schema: "schedule",
                table: "dog_options",
                column: "WeekId");

            migrationBuilder.CreateIndex(
                name: "IX_dog_picks_BallotId",
                schema: "picks",
                table: "dog_picks",
                column: "BallotId");

            migrationBuilder.CreateIndex(
                name: "IX_games_WeekId",
                schema: "schedule",
                table: "games",
                column: "WeekId");

            migrationBuilder.CreateIndex(
                name: "IX_pick_changes_BallotId",
                schema: "picks",
                table: "pick_changes",
                column: "BallotId");

            migrationBuilder.CreateIndex(
                name: "IX_team_picks_BallotId",
                schema: "picks",
                table: "team_picks",
                column: "BallotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dog_options",
                schema: "schedule");

            migrationBuilder.DropTable(
                name: "dog_picks",
                schema: "picks");

            migrationBuilder.DropTable(
                name: "games",
                schema: "schedule");

            migrationBuilder.DropTable(
                name: "pick_changes",
                schema: "picks");

            migrationBuilder.DropTable(
                name: "team_picks",
                schema: "picks");
        }
    }
}
