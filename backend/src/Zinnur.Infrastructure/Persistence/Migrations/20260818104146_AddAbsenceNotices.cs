using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAbsenceNotices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AbsenceNotices",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<long>(type: "bigint", nullable: false),
                    GroupId = table.Column<long>(type: "bigint", nullable: false),
                    SessionId = table.Column<long>(type: "bigint", nullable: false),
                    SessionStart = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    SentById = table.Column<long>(type: "bigint", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    ToTelegram = table.Column<bool>(type: "boolean", nullable: false),
                    OutboxKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbsenceNotices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbsenceNotices_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AbsenceNotices_LiveSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "LiveSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AbsenceNotices_Users_SentById",
                        column: x => x.SentById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AbsenceNotices_Users_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceNotices_GroupId",
                table: "AbsenceNotices",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceNotices_OutboxKey",
                table: "AbsenceNotices",
                column: "OutboxKey");

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceNotices_SentAt",
                table: "AbsenceNotices",
                column: "SentAt");

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceNotices_SentById",
                table: "AbsenceNotices",
                column: "SentById");

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceNotices_SessionId_StudentId",
                table: "AbsenceNotices",
                columns: new[] { "SessionId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceNotices_StudentId",
                table: "AbsenceNotices",
                column: "StudentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbsenceNotices");
        }
    }
}
