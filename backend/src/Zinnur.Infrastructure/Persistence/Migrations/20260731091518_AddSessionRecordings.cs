using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionRecordings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecordingWebhookEvents",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordingWebhookEvents", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "SessionRecordings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SessionId = table.Column<long>(type: "bigint", nullable: false),
                    RequestedBy = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    EgressId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ObjectKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    StopRequestedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionRecordings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionRecordings_LiveSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "LiveSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SessionRecordings_Users_RequestedBy",
                        column: x => x.RequestedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecordingWebhookEvents_ReceivedAt",
                table: "RecordingWebhookEvents",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SessionRecordings_RequestedBy",
                table: "SessionRecordings",
                column: "RequestedBy");

            migrationBuilder.CreateIndex(
                name: "IX_SessionRecordings_SessionId_Id",
                table: "SessionRecordings",
                columns: new[] { "SessionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SessionRecordings_Status_LastAttemptAt",
                table: "SessionRecordings",
                columns: new[] { "Status", "LastAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "UX_SessionRecordings_EgressId",
                table: "SessionRecordings",
                column: "EgressId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecordingWebhookEvents");

            migrationBuilder.DropTable(
                name: "SessionRecordings");
        }
    }
}
