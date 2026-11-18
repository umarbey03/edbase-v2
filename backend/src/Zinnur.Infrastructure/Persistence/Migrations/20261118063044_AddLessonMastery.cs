using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLessonMastery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LessonMasteries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<long>(type: "bigint", nullable: false),
                    ModuleLessonId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DecidedById = table.Column<long>(type: "bigint", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonMasteries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonMasteries_ModuleLessons_ModuleLessonId",
                        column: x => x.ModuleLessonId,
                        principalTable: "ModuleLessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LessonMasteries_Users_DecidedById",
                        column: x => x.DecidedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LessonMasteries_Users_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LessonMasteryEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<long>(type: "bigint", nullable: false),
                    ModuleLessonId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ActorId = table.Column<long>(type: "bigint", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonMasteryEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonMasteryEvents_ModuleLessons_ModuleLessonId",
                        column: x => x.ModuleLessonId,
                        principalTable: "ModuleLessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LessonMasteryEvents_Users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LessonMasteryEvents_Users_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LessonMasteries_DecidedById",
                table: "LessonMasteries",
                column: "DecidedById");

            migrationBuilder.CreateIndex(
                name: "IX_LessonMasteries_ModuleLessonId",
                table: "LessonMasteries",
                column: "ModuleLessonId");

            migrationBuilder.CreateIndex(
                name: "UX_LessonMasteries_Student_Lesson",
                table: "LessonMasteries",
                columns: new[] { "StudentId", "ModuleLessonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LessonMasteryEvents_ActorId",
                table: "LessonMasteryEvents",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonMasteryEvents_ModuleLessonId",
                table: "LessonMasteryEvents",
                column: "ModuleLessonId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonMasteryEvents_Student_Lesson_At",
                table: "LessonMasteryEvents",
                columns: new[] { "StudentId", "ModuleLessonId", "At" });

            // ════════════════════════════════════════════════════════════
            // 🔴 MAVJUD MUSTAQIL O'QUVCHILAR QULFLANIB QOLMASIN.
            // ════════════════════════════════════════════════════════════
            //
            // Yangi qoida: dars kurator «o'zlashtirdi» demaguncha tugallanmaydi.
            // Qoida kiritilgan kuni kursning o'rtasida turgan o'quvchi uchun bu
            // o'tgan darslarining HAMMASINI bir zumda yopib qo'yardi — u pul
            // to'lagan va ishini bajargan darslarni qayta ko'ra olmasdi.
            //
            // Shu sababli o'quvchi ENG UZOQ yetib kelgan (ishini bajargan,
            // kontentli) darsgacha bo'lgan, ishi bajarilgan darslar
            // «o'zlashtirilgan» deb yoziladi — izoh bilan, jurnalga ham.
            // Qarorni yozgan xodim — o'quvchiga birinchi obunani bergan xodim
            // (haqiqiy odam; «tizim» foydalanuvchisi yo'q).
            //
            // ★ BUNDAN KEYINGI darslar uchun qoida qat'iy: hech narsa avtomatik
            //   belgilanmaydi. Bo'sh bazada so'rov hech narsa qilmaydi.
            migrationBuilder.Sql("""
                WITH self_study AS (
                    SELECT DISTINCT ON (p."StudentId")
                        p."StudentId", p."CourseId", p."CreatedById"
                    FROM "SubscriptionPeriods" p
                    WHERE p."CancelledAt" IS NULL
                      AND NOT EXISTS (
                          SELECT 1 FROM "GroupMembers" gm
                          WHERE gm."StudentId" = p."StudentId" AND gm."Status" = 0)
                    ORDER BY p."StudentId", p."EndsAt" DESC
                ),
                first_grantor AS (
                    SELECT DISTINCT ON (p."StudentId") p."StudentId", p."CreatedById"
                    FROM "SubscriptionPeriods" p
                    ORDER BY p."StudentId", p."CreatedAt", p."Id"
                ),
                ranked AS (
                    SELECT s."StudentId", l."Id" AS "LessonId",
                           ROW_NUMBER() OVER (
                               PARTITION BY s."StudentId"
                               ORDER BY m."Position", l."ModuleId", l."Position", l."Id") AS rn
                    FROM self_study s
                    JOIN "Modules" m ON m."CourseId" = s."CourseId"
                    JOIN "ModuleLessons" l ON l."ModuleId" = m."Id"
                ),
                facts AS (
                    SELECT r."StudentId", r."LessonId", r.rn,
                        EXISTS (SELECT 1 FROM "LessonAssets" a
                                WHERE a."LessonId" = r."LessonId" AND a."Kind" = 0) AS has_video,
                        EXISTS (SELECT 1 FROM "LessonProgress" lp
                                WHERE lp."StudentId" = r."StudentId" AND lp."ModuleLessonId" = r."LessonId"
                                  AND lp."VideoWatchedAt" IS NOT NULL) AS watched,
                        EXISTS (SELECT 1 FROM "Assignments" asg
                                WHERE asg."ModuleLessonId" = r."LessonId") AS has_assignment,
                        EXISTS (SELECT 1 FROM "Submissions" sb
                                JOIN "Assignments" asg ON asg."Id" = sb."AssignmentId"
                                WHERE sb."StudentId" = r."StudentId" AND asg."ModuleLessonId" = r."LessonId") AS submitted,
                        EXISTS (SELECT 1 FROM "Tests" t
                                WHERE t."ModuleLessonId" = r."LessonId" AND t."IsPublished") AS has_test,
                        EXISTS (SELECT 1 FROM "TestAttempts" ta
                                JOIN "Tests" t ON t."Id" = ta."TestId"
                                WHERE ta."StudentId" = r."StudentId" AND ta."Status" = 1
                                  AND t."ModuleLessonId" = r."LessonId") AS taken
                    FROM ranked r
                ),
                done AS (
                    SELECT f.*,
                        (NOT f.has_video OR f.watched)
                        AND (NOT f.has_assignment OR f.submitted)
                        AND (NOT f.has_test OR f.taken) AS work_done,
                        (f.has_video OR f.has_assignment OR f.has_test) AS has_content
                    FROM facts f
                ),
                furthest AS (
                    SELECT d."StudentId", MAX(d.rn) AS rn
                    FROM done d
                    WHERE d.work_done AND d.has_content
                    GROUP BY d."StudentId"
                ),
                grandfathered AS (
                    SELECT d."StudentId", d."LessonId", g."CreatedById"
                    FROM done d
                    JOIN furthest fu ON fu."StudentId" = d."StudentId" AND d.rn <= fu.rn
                    JOIN first_grantor g ON g."StudentId" = d."StudentId"
                    WHERE d.work_done
                ),
                inserted AS (
                    INSERT INTO "LessonMasteries"
                        ("StudentId", "ModuleLessonId", "Status", "Note", "DecidedById", "DecidedAt", "CreatedAt")
                    SELECT gf."StudentId", gf."LessonId", 0,
                           'Qoida kiritilishidan oldin bajarilgan — avtomatik saqlandi (2026-11-18).',
                           gf."CreatedById", now(), now()
                    FROM grandfathered gf
                    ON CONFLICT DO NOTHING
                    RETURNING "StudentId", "ModuleLessonId", "Note", "DecidedById"
                )
                INSERT INTO "LessonMasteryEvents"
                    ("StudentId", "ModuleLessonId", "Kind", "Note", "ActorId", "At", "CreatedAt")
                SELECT i."StudentId", i."ModuleLessonId", 0, i."Note", i."DecidedById", now(), now()
                FROM inserted i;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LessonMasteries");

            migrationBuilder.DropTable(
                name: "LessonMasteryEvents");
        }
    }
}
