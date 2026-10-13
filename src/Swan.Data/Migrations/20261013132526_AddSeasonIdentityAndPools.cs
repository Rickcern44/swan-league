using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swan.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSeasonIdentityAndPools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSimulation",
                schema: "league",
                table: "seasons",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                schema: "league",
                table: "seasons",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Pools",
                schema: "league",
                table: "seasons",
                type: "text",
                nullable: false,
                defaultValueSql: "'[]'");

            migrationBuilder.AddColumn<int>(
                name: "Year",
                schema: "league",
                table: "seasons",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSimulation",
                schema: "league",
                table: "seasons");

            migrationBuilder.DropColumn(
                name: "Name",
                schema: "league",
                table: "seasons");

            migrationBuilder.DropColumn(
                name: "Pools",
                schema: "league",
                table: "seasons");

            migrationBuilder.DropColumn(
                name: "Year",
                schema: "league",
                table: "seasons");
        }
    }
}
