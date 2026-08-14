using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Wave4_GroupCategory_ChatAttachments_FeedbackFiles_Questions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssignmentGraderRole",
                table: "Groups",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "CategoryId",
                table: "Groups",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QuestionResponderRole",
                table: "Groups",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "GraderRole",
                table: "Assignments",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GroupCategories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GroupChatAttachments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MessageId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    DurationSec = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupChatAttachments", x => x.Id);
                    table.CheckConstraint("CK_GroupChatAttachments_Kind", "\"Kind\" IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_GroupChatAttachments_GroupChatMessages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "GroupChatMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubmissionFeedbackFiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SubmissionId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedById = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubmissionFeedbackFiles", x => x.Id);
                    table.CheckConstraint("CK_SubmissionFeedbackFiles_Kind", "\"Kind\" IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_SubmissionFeedbackFiles_Submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "Submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SubmissionFeedbackFiles_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Groups_CategoryId",
                table: "Groups",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_DirectMessages_LessonQuestions",
                table: "DirectMessages",
                columns: new[] { "StaffId", "Id" },
                filter: " \"ModuleLessonId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GroupCategories_IsActive_Position",
                table: "GroupCategories",
                columns: new[] { "IsActive", "Position" });

            migrationBuilder.CreateIndex(
                name: "UX_GroupCategories_Name",
                table: "GroupCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupChatAttachments_MessageId_Position",
                table: "GroupChatAttachments",
                columns: new[] { "MessageId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionFeedbackFiles_CreatedById",
                table: "SubmissionFeedbackFiles",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionFeedbackFiles_SubmissionId",
                table: "SubmissionFeedbackFiles",
                column: "SubmissionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Groups_GroupCategories_CategoryId",
                table: "Groups",
                column: "CategoryId",
                principalTable: "GroupCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Groups_GroupCategories_CategoryId",
                table: "Groups");

            migrationBuilder.DropTable(
                name: "GroupCategories");

            migrationBuilder.DropTable(
                name: "GroupChatAttachments");

            migrationBuilder.DropTable(
                name: "SubmissionFeedbackFiles");

            migrationBuilder.DropIndex(
                name: "IX_Groups_CategoryId",
                table: "Groups");

            migrationBuilder.DropIndex(
                name: "IX_DirectMessages_LessonQuestions",
                table: "DirectMessages");

            migrationBuilder.DropColumn(
                name: "AssignmentGraderRole",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "QuestionResponderRole",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "GraderRole",
                table: "Assignments");
        }
    }
}
