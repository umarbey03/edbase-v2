using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPenaltyCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CategoryId",
                table: "Penalties",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Quantity",
                table: "Penalties",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PenaltyCategories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PerUnit = table.Column<bool>(type: "boolean", nullable: false),
                    UnitLabel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SystemKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenaltyCategories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Penalties_CategoryId",
                table: "Penalties",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "UX_PenaltyCategories_Label",
                table: "PenaltyCategories",
                column: "Label",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PenaltyCategories_SystemKey",
                table: "PenaltyCategories",
                column: "SystemKey",
                unique: true,
                filter: " \"SystemKey\" IS NOT NULL ");

            migrationBuilder.AddForeignKey(
                name: "FK_Penalties_PenaltyCategories_CategoryId",
                table: "Penalties",
                column: "CategoryId",
                principalTable: "PenaltyCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ═══════════════════════════════════════════════════════════
            // MA'LUMOTNI KO'CHIRISH: tariflar sozlamalardan katalogga.
            //
            // ★ NIMA UCHUN MIGRATSIYA ICHIDA: `penalty.late_per_minute`
            //   va `penalty.missed_lesson` sozlamalari SHU migratsiya
            //   bilan birga ishlatilishdan chiqadi. Qiymat ko'chirilmasa,
            //   yangi kod ishga tushgan zahoti tarif `0` bo'lib qolardi
            //   va avtomatik jarima JIMGINA yozilmay qo'yardi.
            //
            // ★ `COALESCE` — sozlama umuman yo'q bo'lgan (yangi) bazada
            //   ham qator yaratilsin: administrator uni panelda ko'rib,
            //   summani o'zi kiritadi.
            // ═══════════════════════════════════════════════════════════
            migrationBuilder.Sql("""
                INSERT INTO "PenaltyCategories"
                    ("Label", "Amount", "PerUnit", "UnitLabel", "IsActive", "SystemKey", "CreatedAt")
                VALUES (
                    'Darsga kechikish',
                    COALESCE((SELECT NULLIF("Value", '')::numeric
                              FROM "AppSettings" WHERE "Key" = 'penalty.late_per_minute'), 0),
                    TRUE, 'daqiqa', TRUE, 'late_start', NOW()
                ), (
                    'Dars o''tilmadi',
                    COALESCE((SELECT NULLIF("Value", '')::numeric
                              FROM "AppSettings" WHERE "Key" = 'penalty.missed_lesson'), 0),
                    FALSE, NULL, TRUE, 'missed_lesson', NOW()
                );
                """);

            // Mavjud jarimalarni yangi tariflarga bog'lash — oylik
            // hisobotda ular ham nom bilan chiqsin. Summasi TEGILMAYDI
            // (u yaratilganda muzlatilgan).
            migrationBuilder.Sql("""
                UPDATE "Penalties" p
                SET "CategoryId" = c."Id",
                    "Quantity" = CASE WHEN c."PerUnit" THEN p."LateMinutes" ELSE NULL END
                FROM "PenaltyCategories" c
                WHERE p."CategoryId" IS NULL
                  AND c."SystemKey" = CASE p."Kind"
                        WHEN 0 THEN 'late_start'
                        WHEN 1 THEN 'missed_lesson'
                      END;
                """);

            // Endi bu sozlamalar hech kim tomonidan o'qilmaydi —
            // qoldirilsa, administrator ularni tahrirlab, hech narsa
            // o'zgarmaganiga hayron bo'lardi.
            migrationBuilder.Sql("""
                DELETE FROM "AppSettings"
                WHERE "Key" IN ('penalty.late_per_minute', 'penalty.missed_lesson');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Tariflarni sozlamalarga QAYTARISH — jadval o'chirilishidan
            // OLDIN. Aks holda orqaga qaytgan muhitda avtomatik jarima
            // tarifsiz qolardi.
            migrationBuilder.Sql("""
                INSERT INTO "AppSettings" ("Key", "Value", "UpdatedAt")
                SELECT
                    CASE "SystemKey"
                        WHEN 'late_start' THEN 'penalty.late_per_minute'
                        ELSE 'penalty.missed_lesson'
                    END,
                    "Amount"::text,
                    NOW()
                FROM "PenaltyCategories"
                WHERE "SystemKey" IN ('late_start', 'missed_lesson')
                ON CONFLICT ("Key") DO UPDATE SET "Value" = EXCLUDED."Value";
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Penalties_PenaltyCategories_CategoryId",
                table: "Penalties");

            migrationBuilder.DropTable(
                name: "PenaltyCategories");

            migrationBuilder.DropIndex(
                name: "IX_Penalties_CategoryId",
                table: "Penalties");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Penalties");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "Penalties");
        }
    }
}
