using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swan.Data.Migrations
{
    /// <inheritdoc />
    public partial class AlignEfModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CoachPickVisibilityOverrideReason",
                schema: "schedule",
                table: "weeks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasCoachPickVisibilityOverride",
                schema: "schedule",
                table: "weeks",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoachPickVisibilityOverrideReason",
                schema: "schedule",
                table: "weeks");

            migrationBuilder.DropColumn(
                name: "HasCoachPickVisibilityOverride",
                schema: "schedule",
                table: "weeks");
        }
    }
}
