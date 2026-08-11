using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Wave1_GroupVideoStart_UserProfile_LessonAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TelegramLinkedAt",
                table: "Users",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TelegramUsername",
                table: "Users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "ModuleLessons",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "VideoStartLessonId",
                table: "Groups",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssignmentAttachments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AssignmentId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    DurationSec = table.Column<int>(type: "integer", nullable: true),
                    CreatedById = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssignmentAttachments", x => x.Id);
                    table.CheckConstraint("CK_AssignmentAttachments_Kind", "\"Kind\" IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_AssignmentAttachments_Assignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "Assignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssignmentAttachments_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LessonAssets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LessonId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ObjectKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    DurationSec = table.Column<int>(type: "integer", nullable: true),
                    Width = table.Column<int>(type: "integer", nullable: true),
                    Height = table.Column<int>(type: "integer", nullable: true),
                    CreatedById = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonAssets", x => x.Id);
                    table.CheckConstraint("CK_LessonAssets_Kind", "\"Kind\" IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_LessonAssets_ModuleLessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "ModuleLessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LessonAssets_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StudentNotes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<long>(type: "bigint", nullable: false),
                    AuthorId = table.Column<long>(type: "bigint", nullable: false),
                    GroupId = table.Column<long>(type: "bigint", nullable: true),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentNotes_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_StudentNotes_Users_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentNotes_Users_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TelegramUnlinkAudits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    ActorId = table.Column<long>(type: "bigint", nullable: false),
                    OldTelegramId = table.Column<long>(type: "bigint", nullable: false),
                    OldTelegramUsername = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramUnlinkAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TelegramUnlinkAudits_Users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TelegramUnlinkAudits_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Groups_VideoStartLessonId",
                table: "Groups",
                column: "VideoStartLessonId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembers_GroupId_Status",
                table: "GroupMembers",
                columns: new[] { "GroupId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentAttachments_AssignmentId_Position",
                table: "AssignmentAttachments",
                columns: new[] { "AssignmentId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentAttachments_CreatedById",
                table: "AssignmentAttachments",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_LessonAssets_CreatedById",
                table: "LessonAssets",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_LessonAssets_LessonId_Position",
                table: "LessonAssets",
                columns: new[] { "LessonId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentNotes_AuthorId",
                table: "StudentNotes",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentNotes_GroupId",
                table: "StudentNotes",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentNotes_StudentId_Id",
                table: "StudentNotes",
                columns: new[] { "StudentId", "Id" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_TelegramUnlinkAudits_ActorId",
                table: "TelegramUnlinkAudits",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_TelegramUnlinkAudits_UserId_Id",
                table: "TelegramUnlinkAudits",
                columns: new[] { "UserId", "Id" },
                descending: new[] { false, true });

            migrationBuilder.AddForeignKey(
                name: "FK_Groups_ModuleLessons_VideoStartLessonId",
                table: "Groups",
                column: "VideoStartLessonId",
                principalTable: "ModuleLessons",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ================================================================
            // 🔴 BACKFILL — `Assignments.ImageKey` -> `AssignmentAttachments`
            // ================================================================
            //
            // ★ NIMA UCHUN MAJBURIY: mavjud vazifalarning SHART RASMI faqat
            //   `Assignments.ImageKey` ustunida yashaydi. Yangi UI esa faqat
            //   `attachments` bilan ishlaydi — ya'ni bu blok bo'lmasa
            //   o'quv bo'limi kiritgan barcha shart rasmlari ekrandan
            //   JIMGINA YO'QOLADI (bazada qoladi, lekin hech kim ko'rmaydi).
            //
            // ⚠️ TARTIB MUHIM: bu chaqiruv `Up()` ning OXIRIDA turishi shart —
            //   `AssignmentAttachments` jadvali yuqorida yaratiladi. Yuqoriga
            //   ko'chirilsa "relation does not exist" bilan yiqiladi.
            //
            // ⚠️ QAYERDAN KELDI: bu blok `Wave1_LessonKindAssetsAndAttachments`
            //   migratsiyasidan KO'CHIRILDI. Uchta parallel agent (C/E/G)
            //   bir xil ota-snapshot ustiga uchta migratsiya yasagan edi;
            //   integratsiyada uchalasi o'chirilib shu BITTA bog'in
            //   generatsiya qilindi. Backfill AVTOMATIK generatsiya
            //   QILINMAYDI (u model o'zgarishi emas, MA'LUMOT ko'chirish) —
            //   shuning uchun qo'lda ko'chirildi. Kelajakda migratsiyalar
            //   yana birlashtirilsa SHU BLOK ham ko'chirilishi kerak.
            //
            // `Kind = 0` (Image): eski ustun ATAYLAB faqat rasm uchun edi
            // (nomi ham `ImageKey`).
            //
            // `ContentType` — `image/jpeg` deb TAXMIN QILINMAYDI. Eski
            // yozuvda haqiqiy MIME saqlanmagan, shuning uchun kalitning
            // KENGAYTMASIDAN aniqlanadi; noma'lum bo'lsa
            // `application/octet-stream`. O'qish yo'li baribir bazadagi
            // turni ustun qo'yadi, ya'ni noto'g'ri taxmin brauzerda
            // "rasm ochilmadi" holatini yasardi.
            //
            // `SizeBytes = 0` — haqiqiy hajm NOMA'LUM (u hech qachon
            // yozilmagan). Nol qiymat `Validate()` dan o'tmaydi, lekin u
            // faqat YANGI yozuvlarga qo'llanadi; bu yerda esa nol "hajmi
            // noma'lum" degan ROSTGO'Y qiymat — taxmin qilingan raqamdan
            // yaxshi. ⚠️ OQIBATI: `Range` so'rovi bu yozuvlar uchun
            // ishlamaydi (`RangeHeader` nol hajmda to'liq javob beradi) —
            // rasm uchun bu muhim emas.
            //
            // `CreatedAt` — vazifaning O'ZINING yaratilgan vaqti: rasm
            // vazifa bilan birga kiritilgan deb hisoblash haqiqatga eng
            // yaqin taxmin.
            //
            // `WHERE` shartlari: bo'sh/probel kalitlar tashlab yuboriladi va
            // `NOT EXISTS` bilan TAKROR yozuv oldi olinadi — migratsiya
            // qayta yurgizilsa (yoki integrator uni boshqa bog'inga
            // ko'chirsa) ikkinchi nusxa paydo bo'lmaydi.
            migrationBuilder.Sql(ImageKeyBackfillSql);
        }

        /// <summary>
        /// 🔴 BACKFILL SQL — ATAYLAB OSHKOR (`public const`).
        ///
        /// IKKI SABAB:
        ///
        ///  1) TESTLANADI. Integratsiya testi AYNAN shu satrni bajaradi va
        ///     natijani tekshiradi (xaritalash to'g'rimi, takror yozuv
        ///     paydo bo'lmaydimi). Test o'z nusxasini saqlasa, migratsiya
        ///     o'zgarganda test eski SQL'ni tekshirib "yashil" bo'lib
        ///     qolardi — ya'ni eng qimmat turdagi yolg'on.
        ///     Test: `AssignmentAttachmentTests.Backfill_*`.
        ///
        ///  2) INTEGRATOR KO'CHIRADI. Migratsiyalar birlashtirilganda bu
        ///     blok AVTOMATIK ko'chmaydi (u model o'zgarishi emas, MA'LUMOT
        ///     ko'chirish). Nomlangan doimiy bo'lsa uni topish va ko'chirish
        ///     oson, o'tkazib yuborish esa qiyin: test doimiyning nomiga
        ///     BOG'LANGAN, ya'ni ko'chirilmasa BUILD yiqiladi (jim
        ///     ma'lumot yo'qolishidan ko'ra shovqinli xato yaxshi).
        /// </summary>
        public const string ImageKeyBackfillSql = """
            INSERT INTO "AssignmentAttachments"
                ("AssignmentId", "Kind", "Position", "ObjectKey",
                 "ContentType", "SizeBytes", "DurationSec",
                 "CreatedById", "CreatedAt", "UpdatedAt")
            SELECT
                a."Id",
                0,
                0,
                a."ImageKey",
                CASE
                    WHEN lower(a."ImageKey") LIKE '%.png'  THEN 'image/png'
                    WHEN lower(a."ImageKey") LIKE '%.webp' THEN 'image/webp'
                    WHEN lower(a."ImageKey") LIKE '%.gif'  THEN 'image/gif'
                    WHEN lower(a."ImageKey") LIKE '%.heic' THEN 'image/heic'
                    WHEN lower(a."ImageKey") LIKE '%.jpg'  THEN 'image/jpeg'
                    WHEN lower(a."ImageKey") LIKE '%.jpeg' THEN 'image/jpeg'
                    ELSE 'application/octet-stream'
                END,
                0,
                NULL,
                a."CreatedById",
                a."CreatedAt",
                NULL
            FROM "Assignments" a
            WHERE a."ImageKey" IS NOT NULL
              AND btrim(a."ImageKey") <> ''
              AND NOT EXISTS (
                  SELECT 1 FROM "AssignmentAttachments" x
                  WHERE x."AssignmentId" = a."Id"
                    AND x."ObjectKey" = a."ImageKey"
              );
            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Groups_ModuleLessons_VideoStartLessonId",
                table: "Groups");

            migrationBuilder.DropTable(
                name: "AssignmentAttachments");

            migrationBuilder.DropTable(
                name: "LessonAssets");

            migrationBuilder.DropTable(
                name: "StudentNotes");

            migrationBuilder.DropTable(
                name: "TelegramUnlinkAudits");

            migrationBuilder.DropIndex(
                name: "IX_Groups_VideoStartLessonId",
                table: "Groups");

            migrationBuilder.DropIndex(
                name: "IX_GroupMembers_GroupId_Status",
                table: "GroupMembers");

            migrationBuilder.DropColumn(
                name: "TelegramLinkedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TelegramUsername",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "ModuleLessons");

            migrationBuilder.DropColumn(
                name: "VideoStartLessonId",
                table: "Groups");
        }
    }
}
