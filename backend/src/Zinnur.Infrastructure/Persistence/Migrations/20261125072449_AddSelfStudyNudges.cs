using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSelfStudyNudges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SelfStudyNudgePreferences",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<long>(type: "bigint", nullable: false),
                    OptedOutAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    SnoozedUntil = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SelfStudyNudgePreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SelfStudyNudgePreferences_Users_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SelfStudyNudges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<long>(type: "bigint", nullable: false),
                    RecipientId = table.Column<long>(type: "bigint", nullable: true),
                    Stage = table.Column<int>(type: "integer", nullable: false),
                    AnchorAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    Variant = table.Column<int>(type: "integer", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    ToTelegram = table.Column<bool>(type: "boolean", nullable: false),
                    Response = table.Column<int>(type: "integer", nullable: true),
                    RespondedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SelfStudyNudges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SelfStudyNudges_Users_RecipientId",
                        column: x => x.RecipientId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SelfStudyNudges_Users_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_SelfStudyNudgePreferences_StudentId",
                table: "SelfStudyNudgePreferences",
                column: "StudentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SelfStudyNudges_RecipientId",
                table: "SelfStudyNudges",
                column: "RecipientId");

            migrationBuilder.CreateIndex(
                name: "IX_SelfStudyNudges_SentAt",
                table: "SelfStudyNudges",
                column: "SentAt");

            migrationBuilder.CreateIndex(
                name: "UX_SelfStudyNudges_Student_Stage_Anchor",
                table: "SelfStudyNudges",
                columns: new[] { "StudentId", "Stage", "AnchorAt" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SelfStudyNudgePreferences");

            migrationBuilder.DropTable(
                name: "SelfStudyNudges");
        }
    }
}
