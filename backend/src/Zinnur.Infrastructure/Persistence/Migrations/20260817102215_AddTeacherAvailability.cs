using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTeacherAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CallbackData",
                table: "MessageOutbox",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "OriginalHostId",
                table: "LiveSessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TeacherDailyCheckins",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TeacherId = table.Column<long>(type: "bigint", nullable: false),
                    CheckinDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    RespondedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    DeclineReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UnavailableDays = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherDailyCheckins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeacherDailyCheckins_Users_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SessionCoverageRequests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SessionId = table.Column<long>(type: "bigint", nullable: false),
                    CheckinId = table.Column<long>(type: "bigint", nullable: false),
                    OriginalHostId = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ResolvedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionCoverageRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionCoverageRequests_LiveSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "LiveSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SessionCoverageRequests_TeacherDailyCheckins_CheckinId",
                        column: x => x.CheckinId,
                        principalTable: "TeacherDailyCheckins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionCoverageRequests_Users_OriginalHostId",
                        column: x => x.OriginalHostId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionCoverageRequests_Users_ResolvedByUserId",
                        column: x => x.ResolvedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TeacherCheckinAffectedSessions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CheckinId = table.Column<long>(type: "bigint", nullable: false),
                    SessionId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherCheckinAffectedSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeacherCheckinAffectedSessions_LiveSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "LiveSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeacherCheckinAffectedSessions_TeacherDailyCheckins_Checkin~",
                        column: x => x.CheckinId,
                        principalTable: "TeacherDailyCheckins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubstituteOffers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CoverageRequestId = table.Column<long>(type: "bigint", nullable: false),
                    CandidateTeacherId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    RespondedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubstituteOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubstituteOffers_SessionCoverageRequests_CoverageRequestId",
                        column: x => x.CoverageRequestId,
                        principalTable: "SessionCoverageRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SubstituteOffers_Users_CandidateTeacherId",
                        column: x => x.CandidateTeacherId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LiveSessions_OriginalHostId",
                table: "LiveSessions",
                column: "OriginalHostId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionCoverageRequests_CheckinId",
                table: "SessionCoverageRequests",
                column: "CheckinId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionCoverageRequests_OriginalHostId",
                table: "SessionCoverageRequests",
                column: "OriginalHostId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionCoverageRequests_ResolvedByUserId",
                table: "SessionCoverageRequests",
                column: "ResolvedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionCoverageRequests_SessionId_Status",
                table: "SessionCoverageRequests",
                columns: new[] { "SessionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SubstituteOffers_CandidateTeacherId",
                table: "SubstituteOffers",
                column: "CandidateTeacherId");

            migrationBuilder.CreateIndex(
                name: "UX_SubstituteOffers_CoverageRequestId_CandidateTeacherId",
                table: "SubstituteOffers",
                columns: new[] { "CoverageRequestId", "CandidateTeacherId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeacherCheckinAffectedSessions_SessionId",
                table: "TeacherCheckinAffectedSessions",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "UX_TeacherCheckinAffectedSessions_CheckinId_SessionId",
                table: "TeacherCheckinAffectedSessions",
                columns: new[] { "CheckinId", "SessionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_TeacherDailyCheckins_TeacherId_CheckinDate",
                table: "TeacherDailyCheckins",
                columns: new[] { "TeacherId", "CheckinDate" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LiveSessions_Users_OriginalHostId",
                table: "LiveSessions",
                column: "OriginalHostId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LiveSessions_Users_OriginalHostId",
                table: "LiveSessions");

            migrationBuilder.DropTable(
                name: "SubstituteOffers");

            migrationBuilder.DropTable(
                name: "TeacherCheckinAffectedSessions");

            migrationBuilder.DropTable(
                name: "SessionCoverageRequests");

            migrationBuilder.DropTable(
                name: "TeacherDailyCheckins");

            migrationBuilder.DropIndex(
                name: "IX_LiveSessions_OriginalHostId",
                table: "LiveSessions");

            migrationBuilder.DropColumn(
                name: "CallbackData",
                table: "MessageOutbox");

            migrationBuilder.DropColumn(
                name: "OriginalHostId",
                table: "LiveSessions");
        }
    }
}
