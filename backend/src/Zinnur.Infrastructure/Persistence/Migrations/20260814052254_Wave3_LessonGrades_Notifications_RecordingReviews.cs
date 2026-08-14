using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Wave3_LessonGrades_Notifications_RecordingReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsVisibleToStudents",
                table: "SessionRecordings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VisibilityChangedAt",
                table: "SessionRecordings",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "VisibilityChangedById",
                table: "SessionRecordings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RecordingsVisibleToStudents",
                table: "Groups",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "LessonGradeAudits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SessionId = table.Column<long>(type: "bigint", nullable: false),
                    StudentId = table.Column<long>(type: "bigint", nullable: false),
                    ActorId = table.Column<long>(type: "bigint", nullable: false),
                    OldScore = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    NewScore = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    OldMaxScore = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    NewMaxScore = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    OldComment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    NewComment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonGradeAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonGradeAudits_LiveSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "LiveSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LessonGradeAudits_Users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LessonGradeAudits_Users_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LessonGrades",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SessionId = table.Column<long>(type: "bigint", nullable: false),
                    StudentId = table.Column<long>(type: "bigint", nullable: false),
                    Score = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    MaxScore = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    GradedById = table.Column<long>(type: "bigint", nullable: false),
                    GradedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonGrades", x => x.Id);
                    table.CheckConstraint("CK_LessonGrades_Score", "\"Score\" >= 0\nAND (\"MaxScore\" IS NULL OR \"MaxScore\" > 0)\nAND \"Score\" <= COALESCE(\"MaxScore\", 5)");
                    table.ForeignKey(
                        name: "FK_LessonGrades_LiveSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "LiveSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LessonGrades_Users_GradedById",
                        column: x => x.GradedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LessonGrades_Users_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    EntityId = table.Column<long>(type: "bigint", nullable: true),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SessionReviews",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SessionId = table.Column<long>(type: "bigint", nullable: false),
                    AuthorId = table.Column<long>(type: "bigint", nullable: false),
                    Verdict = table.Column<int>(type: "integer", nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionReviews_LiveSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "LiveSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SessionReviews_Users_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionRecordings_VisibilityChangedById",
                table: "SessionRecordings",
                column: "VisibilityChangedById");

            migrationBuilder.CreateIndex(
                name: "IX_LessonGradeAudits_ActorId",
                table: "LessonGradeAudits",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonGradeAudits_SessionId_StudentId",
                table: "LessonGradeAudits",
                columns: new[] { "SessionId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_LessonGradeAudits_StudentId_CreatedAt",
                table: "LessonGradeAudits",
                columns: new[] { "StudentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LessonGrades_GradedById",
                table: "LessonGrades",
                column: "GradedById");

            migrationBuilder.CreateIndex(
                name: "IX_LessonGrades_StudentId",
                table: "LessonGrades",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "UX_LessonGrades_SessionId_StudentId",
                table: "LessonGrades",
                columns: new[] { "SessionId", "StudentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_User_Read_Created",
                table: "Notifications",
                columns: new[] { "UserId", "ReadAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SessionReviews_AuthorId",
                table: "SessionReviews",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "UX_SessionReviews_SessionId",
                table: "SessionReviews",
                column: "SessionId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SessionRecordings_Users_VisibilityChangedById",
                table: "SessionRecordings",
                column: "VisibilityChangedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SessionRecordings_Users_VisibilityChangedById",
                table: "SessionRecordings");

            migrationBuilder.DropTable(
                name: "LessonGradeAudits");

            migrationBuilder.DropTable(
                name: "LessonGrades");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "SessionReviews");

            migrationBuilder.DropIndex(
                name: "IX_SessionRecordings_VisibilityChangedById",
                table: "SessionRecordings");

            migrationBuilder.DropColumn(
                name: "IsVisibleToStudents",
                table: "SessionRecordings");

            migrationBuilder.DropColumn(
                name: "VisibilityChangedAt",
                table: "SessionRecordings");

            migrationBuilder.DropColumn(
                name: "VisibilityChangedById",
                table: "SessionRecordings");

            migrationBuilder.DropColumn(
                name: "RecordingsVisibleToStudents",
                table: "Groups");
        }
    }
}
