using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLessonReconciliationAndPayroll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FreeLessonReason",
                table: "LiveSessions",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFreeLesson",
                table: "LiveSessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PayrollExcluded",
                table: "LiveSessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "NetAmount",
                table: "LessonCharges",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "SkipReason",
                table: "LessonCharges",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SessionPayouts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SessionId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    AttendedStudents = table.Column<int>(type: "integer", nullable: false),
                    SessionRate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BonusAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RateMissing = table.Column<bool>(type: "boolean", nullable: false),
                    Excluded = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionPayouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionPayouts_LiveSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "LiveSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionPayouts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionPayouts_UserId_SessionId",
                table: "SessionPayouts",
                columns: new[] { "UserId", "SessionId" });

            migrationBuilder.CreateIndex(
                name: "UX_SessionPayouts_SessionId",
                table: "SessionPayouts",
                column: "SessionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SessionPayouts");

            migrationBuilder.DropColumn(
                name: "FreeLessonReason",
                table: "LiveSessions");

            migrationBuilder.DropColumn(
                name: "IsFreeLesson",
                table: "LiveSessions");

            migrationBuilder.DropColumn(
                name: "PayrollExcluded",
                table: "LiveSessions");

            migrationBuilder.DropColumn(
                name: "NetAmount",
                table: "LessonCharges");

            migrationBuilder.DropColumn(
                name: "SkipReason",
                table: "LessonCharges");
        }
    }
}
