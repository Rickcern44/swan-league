using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swan.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFinesAndPayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MissedPickFine",
                schema: "league",
                table: "seasons",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayoutPlans",
                schema: "league",
                table: "seasons",
                type: "text",
                nullable: false,
                defaultValueSql: "'[]'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MissedPickFine",
                schema: "league",
                table: "seasons");

            migrationBuilder.DropColumn(
                name: "PayoutPlans",
                schema: "league",
                table: "seasons");
        }
    }
}
