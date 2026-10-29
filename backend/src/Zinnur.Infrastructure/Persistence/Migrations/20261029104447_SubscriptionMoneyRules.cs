using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionMoneyRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Method",
                table: "SubscriptionPeriods",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<bool>(
                name: "IncludesInteractiveBook",
                table: "SubscriptionPeriods",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // ★ 2026-10-29 (P2-3): allaqachon sotilgan davrlar o'z tarifidagi
            //   darslik belgisini oladi — aks holda Mustaqil+ xaridorlari
            //   darslik ochilganda "false" bilan qolib ketardi.
            migrationBuilder.Sql("""
                UPDATE "SubscriptionPeriods" AS p
                SET "IncludesInteractiveBook" = sp."IncludesInteractiveBook"
                FROM "SubscriptionPlans" AS sp
                WHERE p."PlanId" = sp."Id" AND sp."IncludesInteractiveBook";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IncludesInteractiveBook",
                table: "SubscriptionPeriods");

            // ⚠️ Orqaga qaytishda balansdan to'langan davrlar (`Method IS NULL`)
            //    "Naqd" (0) bo'lib qoladi — haqiqiy manba jurnaldagi
            //    `SubscriptionBalanceUse` yozuvida saqlanadi.
            migrationBuilder.Sql("""UPDATE "SubscriptionPeriods" SET "Method" = 0 WHERE "Method" IS NULL;""");

            migrationBuilder.AlterColumn<int>(
                name: "Method",
                table: "SubscriptionPeriods",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
