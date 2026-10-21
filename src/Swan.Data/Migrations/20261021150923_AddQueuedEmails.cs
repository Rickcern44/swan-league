using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swan.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQueuedEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "queued_emails",
                schema: "communication",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Recipient = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    QueuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_queued_emails", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_queued_emails_SentAt_QueuedAt",
                schema: "communication",
                table: "queued_emails",
                columns: new[] { "SentAt", "QueuedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "queued_emails",
                schema: "communication");
        }
    }
}
