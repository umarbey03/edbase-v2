using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollRuleEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ═══════════════════════════════════════════════════════════════
            // 🔴 `DropTable("TeacherRates")` ATAYLAB PASTGA KO'CHIRILDI
            //
            //    EF uni shu yerga — eng boshiga — qo'ygan edi. Bunda mavjud
            //    stavkalar YANGI jadval yaratilgunga qadar yo'q qilinardi,
            //    ya'ni ma'lumot ko'chirishning imkoni qolmasdi: migratsiyadan
            //    keyin markazda BITTA HAM stavka bo'lmay, barcha darslar
            //    "qoida topilmadi" bo'lib hisoblanardi.
            //
            //    Endi tartib: yangi jadvallar -> MA'LUMOT KO'CHIRISH -> drop.
            //    (Ko'chirish `Up` ning oxirida, izohi o'sha yerda.)
            // ═══════════════════════════════════════════════════════════════

            migrationBuilder.AddColumn<bool>(
                name: "IncludedInSalary",
                table: "SessionPayouts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PayrollMode",
                table: "Groups",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "PayrollRuleId",
                table: "Groups",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PayrollRules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: true),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    CourseId = table.Column<long>(type: "bigint", nullable: true),
                    GroupId = table.Column<long>(type: "bigint", nullable: true),
                    CategoryId = table.Column<long>(type: "bigint", nullable: true),
                    GroupType = table.Column<int>(type: "integer", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AcademicHourMinutes = table.Column<int>(type: "integer", nullable: false),
                    Basis = table.Column<int>(type: "integer", nullable: false),
                    MinStudents = table.Column<int>(type: "integer", nullable: true),
                    MaxStudents = table.Column<int>(type: "integer", nullable: true),
                    MinDurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    PlanAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    PlanReachedPercent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    WeekendHolidayMultiplier = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ActiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ActiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRules", x => x.Id);
                    table.CheckConstraint("CK_PayrollRules_Amounts_NonNegative", "(\"Amount\" >= 0 AND (\"PlanAmount\" IS NULL OR \"PlanAmount\" >= 0))");
                    table.CheckConstraint("CK_PayrollRules_Multiplier", "(\"WeekendHolidayMultiplier\" IS NULL OR \"WeekendHolidayMultiplier\" >= 1)");
                    table.CheckConstraint("CK_PayrollRules_PeriodOrder", "(\"ActiveTo\" IS NULL OR \"ActiveTo\" >= \"ActiveFrom\")");
                    table.CheckConstraint("CK_PayrollRules_PlanPair", "((\"PlanAmount\" IS NULL) = (\"PlanReachedPercent\" IS NULL))");
                    table.ForeignKey(
                        name: "FK_PayrollRules_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollRules_GroupCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "GroupCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollRules_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollRules_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollStudentCoefficients",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    StudentId = table.Column<long>(type: "bigint", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    Percent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedById = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollStudentCoefficients", x => x.Id);
                    table.CheckConstraint("CK_PayrollStudentCoefficients_Percent", "(\"Percent\" >= 0 AND \"Percent\" <= 100)");
                    table.ForeignKey(
                        name: "FK_PayrollStudentCoefficients_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollStudentCoefficients_Users_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayrollStudentCoefficients_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRuleTiers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RuleId = table.Column<long>(type: "bigint", nullable: false),
                    StudentCount = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRuleTiers", x => x.Id);
                    table.CheckConstraint("CK_PayrollRuleTiers_NonNegative", "(\"StudentCount\" >= 0 AND \"Amount\" >= 0)");
                    table.ForeignKey(
                        name: "FK_PayrollRuleTiers_PayrollRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "PayrollRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SessionPayoutLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PayoutId = table.Column<long>(type: "bigint", nullable: false),
                    RuleId = table.Column<long>(type: "bigint", nullable: true),
                    RuleName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Basis = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionPayoutLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionPayoutLines_PayrollRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "PayrollRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SessionPayoutLines_SessionPayouts_PayoutId",
                        column: x => x.PayoutId,
                        principalTable: "SessionPayouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Groups_PayrollRuleId",
                table: "Groups",
                column: "PayrollRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRules_CategoryId",
                table: "PayrollRules",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRules_CourseId",
                table: "PayrollRules",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRules_GroupId",
                table: "PayrollRules",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRules_IsActive_Role_ActiveFrom",
                table: "PayrollRules",
                columns: new[] { "IsActive", "Role", "ActiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRules_UserId",
                table: "PayrollRules",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UX_PayrollRuleTiers_RuleId_StudentCount",
                table: "PayrollRuleTiers",
                columns: new[] { "RuleId", "StudentCount" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollStudentCoefficients_CreatedById",
                table: "PayrollStudentCoefficients",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollStudentCoefficients_StudentId",
                table: "PayrollStudentCoefficients",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "UX_PayrollStudentCoefficients_User_Student_Period",
                table: "PayrollStudentCoefficients",
                columns: new[] { "UserId", "StudentId", "PeriodStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionPayoutLines_PayoutId",
                table: "SessionPayoutLines",
                column: "PayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionPayoutLines_RuleId",
                table: "SessionPayoutLines",
                column: "RuleId");

            migrationBuilder.AddForeignKey(
                name: "FK_Groups_PayrollRules_PayrollRuleId",
                table: "Groups",
                column: "PayrollRuleId",
                principalTable: "PayrollRules",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ═══════════════════════════════════════════════════════════════
            //  MA'LUMOT KO'CHIRISH: TeacherRates -> PayrollRules
            // ═══════════════════════════════════════════════════════════════
            //
            //  Eski jadvalning BITTA qatori to'rttagacha pul ustunini olib
            //  yurardi va ular hammasi birdan qo'llanardi. Yangi modelda har
            //  biri ALOHIDA qoida (sabab `PayrollRule` sinf izohida), shuning
            //  uchun bitta eski qator to'rttagacha yangi qatorga yoyiladi.
            //
            //  ★ NOLGA TENG USTUNLAR KO'CHIRILMAYDI (`> 0` sharti): eski
            //    jadvalda "bu markazda oklad yo'q" 0 bilan ifodalanardi.
            //    Ularni ham ko'chirish har stavka uchun to'rtta qatorli,
            //    aksariyati 0 so'mlik ma'nosiz ro'yxat hosil qilardi.
            //
            //  ★ USTAMA (`WeekendHolidayMultiplier`) FAQAT DARS STAVKASIGA
            //    ko'chiriladi — eski kodda ham u faqat asosiy stavkaga
            //    qo'llanardi (`LessonAccrualService`), bonusga tegilmasdi.
            //
            //  ★ `CreatedAt` MANBADAN olinadi: qoidalar orasidagi tie-break
            //    `ActiveFrom` va `Id` bo'yicha ketadi, lekin yaratilish vaqti
            //    auditda "bu qachon kiritilgan edi" savoliga javob beradi.
            //
            //  Kind qiymatlari (`PayrollRuleKind`):
            //    0 = FixedMonthly, 1 = PerSession,
            //    3 = PerAttendedStudent, 6 = MonthlyPerActiveStudent
            //  Basis 0 = Attended (eski xatti-harakat: faqat kelganlar).

            migrationBuilder.Sql("""
                INSERT INTO "PayrollRules"
                    ("Name", "Kind", "UserId", "Role", "Amount", "AcademicHourMinutes",
                     "Basis", "WeekendHolidayMultiplier", "ActiveFrom", "IsActive",
                     "CreatedAt", "UpdatedAt")
                SELECT
                    'Dars stavkasi (eski #' || r."Id" || ')',
                    1, r."UserId", r."Role", r."PerSessionRate", 45, 0,
                    r."WeekendHolidayMultiplier", r."ActiveFrom", r."IsActive",
                    r."CreatedAt", NULL
                FROM "TeacherRates" r
                WHERE r."PerSessionRate" > 0;

                INSERT INTO "PayrollRules"
                    ("Name", "Kind", "UserId", "Role", "Amount", "AcademicHourMinutes",
                     "Basis", "WeekendHolidayMultiplier", "ActiveFrom", "IsActive",
                     "CreatedAt", "UpdatedAt")
                SELECT
                    'O''quvchi bonusi (eski #' || r."Id" || ')',
                    3, r."UserId", r."Role", r."PerStudentBonusRate", 45, 0,
                    NULL, r."ActiveFrom", r."IsActive", r."CreatedAt", NULL
                FROM "TeacherRates" r
                WHERE r."PerStudentBonusRate" > 0;

                INSERT INTO "PayrollRules"
                    ("Name", "Kind", "UserId", "Role", "Amount", "AcademicHourMinutes",
                     "Basis", "WeekendHolidayMultiplier", "ActiveFrom", "IsActive",
                     "CreatedAt", "UpdatedAt")
                SELECT
                    'Oklad (eski #' || r."Id" || ')',
                    0, r."UserId", r."Role", r."BaseSalary", 45, 0,
                    NULL, r."ActiveFrom", r."IsActive", r."CreatedAt", NULL
                FROM "TeacherRates" r
                WHERE r."BaseSalary" > 0;

                INSERT INTO "PayrollRules"
                    ("Name", "Kind", "UserId", "Role", "Amount", "AcademicHourMinutes",
                     "Basis", "WeekendHolidayMultiplier", "ActiveFrom", "IsActive",
                     "CreatedAt", "UpdatedAt")
                SELECT
                    'Oylik o''quvchi bonusi (eski #' || r."Id" || ')',
                    6, r."UserId", r."Role", r."ActiveStudentBonusRate", 45, 0,
                    NULL, r."ActiveFrom", r."IsActive", r."CreatedAt", NULL
                FROM "TeacherRates" r
                WHERE r."ActiveStudentBonusRate" > 0;
                """);

            // Ma'lumot ko'chirilgandan KEYIN — izoh `Up` boshida.
            migrationBuilder.DropTable(
                name: "TeacherRates");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ═══════════════════════════════════════════════════════════════
            // ⚠️ ORQAGA QAYTISH SXEMANI TIKLAYDI, MA'LUMOTNI EMAS
            //
            //    `TeacherRates` BO'SH holda qayta yaratiladi. Sabab: yangi
            //    modelda bitta eski qator to'rtta qoidaga yoyilgan va ular
            //    keyin alohida tahrirlangan/o'chirilgan bo'lishi mumkin —
            //    ularni bir qatorga qaytarish "qaysi to'rttasi bir vaqtda
            //    bitta stavka edi?" degan javobsiz savolga tayanardi va
            //    jimgina NOTO'G'RI summalar hosil qilardi.
            //
            //    ★ PROD'DA ORQAGA QAYTARISHDAN OLDIN: bazadan zaxira nusxa
            //      oling — `PayrollRules` dagi ma'lumot yo'qoladi.
            //
            //    ★ HAQ TARIXI XAVFSIZ: `SessionPayouts` (va uning
            //      `SessionPayoutLines` tafsiloti) bu yerda o'chmaydi —
            //      o'tgan oylarning summalari o'z joyida qoladi.
            // ═══════════════════════════════════════════════════════════════

            migrationBuilder.DropForeignKey(
                name: "FK_Groups_PayrollRules_PayrollRuleId",
                table: "Groups");

            migrationBuilder.DropTable(
                name: "PayrollRuleTiers");

            migrationBuilder.DropTable(
                name: "PayrollStudentCoefficients");

            migrationBuilder.DropTable(
                name: "SessionPayoutLines");

            migrationBuilder.DropTable(
                name: "PayrollRules");

            migrationBuilder.DropIndex(
                name: "IX_Groups_PayrollRuleId",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "IncludedInSalary",
                table: "SessionPayouts");

            migrationBuilder.DropColumn(
                name: "PayrollMode",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "PayrollRuleId",
                table: "Groups");

            migrationBuilder.CreateTable(
                name: "TeacherRates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: true),
                    ActiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ActiveStudentBonusRate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BaseSalary = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    PerSessionRate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PerStudentBonusRate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    WeekendHolidayMultiplier = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherRates", x => x.Id);
                    table.CheckConstraint("CK_TeacherRates_Multiplier", "(\"WeekendHolidayMultiplier\" IS NULL OR \"WeekendHolidayMultiplier\" >= 1)");
                    table.CheckConstraint("CK_TeacherRates_Rates_NonNegative", "(\"PerSessionRate\" >= 0 AND \"PerStudentBonusRate\" >= 0 AND \"BaseSalary\" >= 0 AND \"ActiveStudentBonusRate\" >= 0)");
                    table.ForeignKey(
                        name: "FK_TeacherRates_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeacherRates_UserId",
                table: "TeacherRates",
                column: "UserId");
        }
    }
}
