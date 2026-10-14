using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swan.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderGameId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProviderGameId",
                schema: "schedule",
                table: "games",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_games_provider_game_id",
                schema: "schedule",
                table: "games",
                column: "ProviderGameId",
                unique: true,
                filter: "\"ProviderGameId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_games_provider_game_id",
                schema: "schedule",
                table: "games");

            migrationBuilder.DropColumn(
                name: "ProviderGameId",
                schema: "schedule",
                table: "games");
        }
    }
}
