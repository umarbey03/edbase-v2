using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationOutboxTelegramAndFinanceIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MessageOutbox",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    RecipientUserId = table.Column<long>(type: "bigint", nullable: true),
                    RecipientAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TemplateKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Body = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageOutbox", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MessageOutbox_Users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TelegramUpdates",
                columns: table => new
                {
                    UpdateId = table.Column<long>(type: "bigint", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramUpdates", x => x.UpdateId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Status_Period",
                table: "Payments",
                columns: new[] { "Status", "Period" })
                .Annotation("Npgsql:IndexInclude", new[] { "Amount", "PaidAmount", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_MessageOutbox_Pending",
                table: "MessageOutbox",
                columns: new[] { "NextAttemptAt", "Id" },
                filter: " \"Status\" = 0 ");

            migrationBuilder.CreateIndex(
                name: "IX_MessageOutbox_Recipient_CreatedAt",
                table: "MessageOutbox",
                columns: new[] { "RecipientUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_MessageOutbox_IdempotencyKey",
                table: "MessageOutbox",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TelegramUpdates_ReceivedAt",
                table: "TelegramUpdates",
                column: "ReceivedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MessageOutbox");

            migrationBuilder.DropTable(
                name: "TelegramUpdates");

            migrationBuilder.DropIndex(
                name: "IX_Payments_Status_Period",
                table: "Payments");
        }
    }
}
