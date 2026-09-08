using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// `Users.Email` USTUNI OLIB TASHLANADI (2026-09-08)
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// Loyiha egasining qarori: markazda email UMUMAN ishlatilmagan.
    /// Tizimga kirish 2026-08-13 dan beri faqat telefon raqami + Telegram
    /// orqali; email esa hech qayerda tekshirilmaydigan, lekin HAR
    /// foydalanuvchi yaratishda majburiy so'raladigan maydon bo'lib
    /// qolgan edi.
    ///
    /// 🔴 BU QAYTARIB BO'LMAYDIGAN MA'LUMOT YO'QOTISHI. Ustundagi
    /// qiymatlar o'chadi. Ular hech qayerda ishlatilmagani uchun yo'qotish
    /// FUNKSIONAL emas, lekin deploy oldidan zaxira olinishi SHART
    /// (`deploy.sh` buni o'zi qiladi va zaxira olinmasa to'xtaydi).
    ///
    /// ── UCHTA OB'YEKT TUSHADI ───────────────────────────────────────────
    ///
    ///   • `IX_Users_Email_Trgm` — pg_trgm GIN indeksi. U XOM SQL bilan
    ///     yaratilgan (`AddUserPhoneNormalizedAndSearchIndexes`), ya'ni EF
    ///     uni modelda KO'RMAYDI va `DropColumn` ga qo'shib bermaydi.
    ///     Postgres ustun tashlanganda uni o'zi ham o'chirardi, lekin
    ///     ATAYLAB oshkora yozilgan: `Down()` uni qaytarishi kerak, aks
    ///     holda orqaga qaytgan tizimda qidiruv jimgina seq scan'ga
    ///     tushardi.
    ///   • `IX_Users_Email` — unikal indeks (EF ko'radi).
    ///   • `Email` ustunining o'zi.
    ///
    /// ── `Down()` HAQIQATAN ISHLAYDI ─────────────────────────────────────
    ///
    /// ⚠️ EF yaratgan standart `Down()` BUZUQ EDI: u ustunni
    /// `NOT NULL DEFAULT ''` bilan qo'shib, ustiga UNIKAL indeks
    /// qurardi — mavjud har bir qatorda qiymat bir xil (`''`) bo'lgani
    /// uchun ikkinchi qatordayoq yiqilardi. Ya'ni qaytish yo'li faqat
    /// BO'SH bazada ishlardi.
    ///
    /// Shuning uchun bu yerda uch qadam: ustun avval NULL bilan
    /// qo'shiladi, so'ng har qatorga TAKRORLANMAS o'rin bosar yoziladi
    /// (`user-{Id}@example.invalid` — `.invalid` RFC 2606 bo'yicha hech
    /// qachon haqiqiy domen bo'lolmaydi), shundan keyingina `NOT NULL`
    /// va unikal indeks qo'yiladi. Qaytarilgan qiymatlar ASL EMAS —
    /// asl emaillar bu migratsiyada butunlay yo'qoladi.
    /// </summary>
    public partial class RemoveUserEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Users_Email_Trgm";""");

            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            // Har qatorga TAKRORLANMAS o'rin bosar (sabab sinf izohida).
            migrationBuilder.Sql("""
                UPDATE "Users"
                SET "Email" = 'user-' || "Id" || '@example.invalid'
                WHERE "Email" IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Users_Email_Trgm"
                    ON "Users" USING gin ("Email" gin_trgm_ops);
                """);
        }
    }
}
