using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInteractiveBooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InteractiveAudio",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InteractiveAudio", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InteractiveBooks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Subtitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CourseId = table.Column<long>(type: "bigint", nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InteractiveBooks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InteractiveBooks_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "InteractiveImportRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BookCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    ActorId = table.Column<long>(type: "bigint", nullable: true),
                    DryRun = table.Column<bool>(type: "boolean", nullable: false),
                    Result = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Report = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InteractiveImportRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InteractiveItemResults",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<long>(type: "bigint", nullable: false),
                    UnitId = table.Column<long>(type: "bigint", nullable: false),
                    VersionId = table.Column<long>(type: "bigint", nullable: false),
                    ItemKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "jsonb", nullable: false),
                    FirstRight = table.Column<int>(type: "integer", nullable: true),
                    FirstTotal = table.Column<int>(type: "integer", nullable: true),
                    FirstAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastRight = table.Column<int>(type: "integer", nullable: true),
                    LastAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InteractiveItemResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InteractiveItemResults_Users_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InteractiveTestAttempts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<long>(type: "bigint", nullable: false),
                    UnitId = table.Column<long>(type: "bigint", nullable: false),
                    VersionId = table.Column<long>(type: "bigint", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    Score = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    MaxScore = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    Answers = table.Column<string>(type: "jsonb", nullable: false),
                    IsFirst = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InteractiveTestAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InteractiveTestAttempts_Users_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InteractiveUnitProgress",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<long>(type: "bigint", nullable: false),
                    UnitId = table.Column<long>(type: "bigint", nullable: false),
                    LastVersionId = table.Column<long>(type: "bigint", nullable: false),
                    BlocksDone = table.Column<string>(type: "jsonb", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    ExerciseRight = table.Column<int>(type: "integer", nullable: false),
                    ExerciseTotal = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InteractiveUnitProgress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InteractiveUnitProgress_Users_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InteractiveUnits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BookId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    TitleUz = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    TitleAr = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ModuleLessonId = table.Column<long>(type: "bigint", nullable: true),
                    CurrentVersionId = table.Column<long>(type: "bigint", nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InteractiveUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InteractiveUnits_InteractiveBooks_BookId",
                        column: x => x.BookId,
                        principalTable: "InteractiveBooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InteractiveUnits_ModuleLessons_ModuleLessonId",
                        column: x => x.ModuleLessonId,
                        principalTable: "ModuleLessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "InteractiveUnitVersions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UnitId = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ContentRaw = table.Column<string>(type: "text", nullable: false),
                    ContentSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Content = table.Column<string>(type: "jsonb", nullable: false),
                    TeacherGuideMd = table.Column<string>(type: "text", nullable: true),
                    TeacherGuideSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    AudioManifest = table.Column<string>(type: "jsonb", nullable: false),
                    Stats = table.Column<string>(type: "jsonb", nullable: false),
                    SourcePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ImportRunId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InteractiveUnitVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InteractiveUnitVersions_InteractiveImportRuns_ImportRunId",
                        column: x => x.ImportRunId,
                        principalTable: "InteractiveImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_InteractiveUnitVersions_InteractiveUnits_UnitId",
                        column: x => x.UnitId,
                        principalTable: "InteractiveUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_InteractiveAudio_Sha256",
                table: "InteractiveAudio",
                column: "Sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InteractiveBooks_CourseId",
                table: "InteractiveBooks",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "UX_InteractiveBooks_Code",
                table: "InteractiveBooks",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InteractiveImportRuns_StartedAt",
                table: "InteractiveImportRuns",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_InteractiveItemResults_UnitId",
                table: "InteractiveItemResults",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_InteractiveItemResults_VersionId",
                table: "InteractiveItemResults",
                column: "VersionId");

            migrationBuilder.CreateIndex(
                name: "UX_InteractiveItemResults_Student_Unit_Item",
                table: "InteractiveItemResults",
                columns: new[] { "StudentId", "UnitId", "ItemKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InteractiveTestAttempts_SubmittedAt",
                table: "InteractiveTestAttempts",
                column: "SubmittedAt");

            migrationBuilder.CreateIndex(
                name: "IX_InteractiveTestAttempts_UnitId",
                table: "InteractiveTestAttempts",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_InteractiveTestAttempts_VersionId",
                table: "InteractiveTestAttempts",
                column: "VersionId");

            migrationBuilder.CreateIndex(
                name: "UX_InteractiveTestAttempts_First",
                table: "InteractiveTestAttempts",
                columns: new[] { "StudentId", "UnitId" },
                unique: true,
                filter: "\"IsFirst\"");

            migrationBuilder.CreateIndex(
                name: "IX_InteractiveUnitProgress_CompletedAt",
                table: "InteractiveUnitProgress",
                column: "CompletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_InteractiveUnitProgress_UnitId",
                table: "InteractiveUnitProgress",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "UX_InteractiveUnitProgress_Student_Unit",
                table: "InteractiveUnitProgress",
                columns: new[] { "StudentId", "UnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InteractiveUnits_CurrentVersionId",
                table: "InteractiveUnits",
                column: "CurrentVersionId");

            migrationBuilder.CreateIndex(
                name: "UX_InteractiveUnits_Book_Code",
                table: "InteractiveUnits",
                columns: new[] { "BookId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_InteractiveUnits_ModuleLessonId",
                table: "InteractiveUnits",
                column: "ModuleLessonId",
                unique: true,
                filter: "\"ModuleLessonId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InteractiveUnitVersions_ImportRunId",
                table: "InteractiveUnitVersions",
                column: "ImportRunId");

            migrationBuilder.CreateIndex(
                name: "IX_InteractiveUnitVersions_Unit_Sha",
                table: "InteractiveUnitVersions",
                columns: new[] { "UnitId", "ContentSha256" });

            migrationBuilder.CreateIndex(
                name: "UX_InteractiveUnitVersions_Unit_Version",
                table: "InteractiveUnitVersions",
                columns: new[] { "UnitId", "Version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_InteractiveItemResults_InteractiveUnitVersions_VersionId",
                table: "InteractiveItemResults",
                column: "VersionId",
                principalTable: "InteractiveUnitVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InteractiveItemResults_InteractiveUnits_UnitId",
                table: "InteractiveItemResults",
                column: "UnitId",
                principalTable: "InteractiveUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_InteractiveTestAttempts_InteractiveUnitVersions_VersionId",
                table: "InteractiveTestAttempts",
                column: "VersionId",
                principalTable: "InteractiveUnitVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InteractiveTestAttempts_InteractiveUnits_UnitId",
                table: "InteractiveTestAttempts",
                column: "UnitId",
                principalTable: "InteractiveUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_InteractiveUnitProgress_InteractiveUnits_UnitId",
                table: "InteractiveUnitProgress",
                column: "UnitId",
                principalTable: "InteractiveUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_InteractiveUnits_InteractiveUnitVersions_CurrentVersionId",
                table: "InteractiveUnits",
                column: "CurrentVersionId",
                principalTable: "InteractiveUnitVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InteractiveUnits_InteractiveUnitVersions_CurrentVersionId",
                table: "InteractiveUnits");

            migrationBuilder.DropTable(
                name: "InteractiveAudio");

            migrationBuilder.DropTable(
                name: "InteractiveItemResults");

            migrationBuilder.DropTable(
                name: "InteractiveTestAttempts");

            migrationBuilder.DropTable(
                name: "InteractiveUnitProgress");

            migrationBuilder.DropTable(
                name: "InteractiveUnitVersions");

            migrationBuilder.DropTable(
                name: "InteractiveImportRuns");

            migrationBuilder.DropTable(
                name: "InteractiveUnits");

            migrationBuilder.DropTable(
                name: "InteractiveBooks");
        }
    }
}
