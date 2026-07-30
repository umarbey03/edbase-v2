using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPhoneNormalizedAndSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Phone",
                table: "Users");

            migrationBuilder.AlterColumn<string>(
                name: "Phone",
                table: "Users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneNormalized",
                table: "Users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // ================================================================
            // QO'LDA QO'SHILGAN (1/3): MAVJUD QATORLARNI TO'LDIRISH
            // ================================================================
            // Unikal indeks qo'yilishidan OLDIN bajarilishi SHART, aks holda
            // mavjud telefonlar `NULL` bo'lib qolardi va telefon bo'yicha
            // qidiruv/kirish ishlamay qolardi.
            //
            // `User.NormalizePhone` mantig'ining SQL nusxasi:
            //   9 xonali            -> '+998' prefiksi
            //   13 xonali, 0 bilan  -> boshidagi nol olib tashlanadi
            //   qolgani             -> '+' + raqamlar
            //
            // DIQQAT: agar bazada normalizatsiyadan keyin BIR XIL bo'lib qoladigan
            // ikkita telefon bo'lsa, quyidagi unikal indeks qurilmaydi va
            // migratsiya XATO beradi. Bu ataylab: jimgina ma'lumot yo'qotgandan
            // ko'ra dublikatni qo'lda hal qilgan afzal.
            migrationBuilder.Sql("""
                UPDATE "Users" AS u
                SET "PhoneNormalized" = CASE
                        WHEN length(d.digits) = 9
                            THEN '+998' || d.digits
                        WHEN length(d.digits) = 13 AND left(d.digits, 1) = '0'
                            THEN '+' || right(d.digits, 12)
                        ELSE '+' || d.digits
                    END
                FROM (
                    SELECT "Id", regexp_replace("Phone", '[^0-9]', '', 'g') AS digits
                    FROM "Users"
                    WHERE "Phone" IS NOT NULL
                ) AS d
                WHERE u."Id" = d."Id"
                  AND d.digits <> ''
                  AND length(d.digits) <= 18;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Users_PhoneNormalized",
                table: "Users",
                column: "PhoneNormalized",
                unique: true,
                filter: "\"PhoneNormalized\" IS NOT NULL");

            // ================================================================
            // QO'LDA QO'SHILGAN (2/3): pg_trgm KENGAYTMASI
            // ================================================================
            // Trigramma indekslari uchun zarur. `IF NOT EXISTS` — kengaytma
            // boshqa migratsiyada ham qo'shilishi mumkin (idempotent bo'lsin).
            migrationBuilder.Sql("""CREATE EXTENSION IF NOT EXISTS pg_trgm;""");

            // ================================================================
            // QO'LDA QO'SHILGAN (3/3): QIDIRUV UCHUN GIN TRIGRAMMA INDEKSLARI
            // ================================================================
            // NIMA UCHUN: CRM qidiruvi `LIKE '%matn%'` shaklida. Naqsh boshida
            // '%' bo'lgani uchun oddiy B-tree indeks UMUMAN ishlamaydi —
            // Postgres butun jadvalni skan qiladi (100 ming yozuvda sekundlar).
            // `pg_trgm` GIN indeksi matnni 3 belgili bo'laklarga (trigramma)
            // ajratib saqlaydi va aynan shu shakldagi qidiruvni tezlashtiradi.
            //
            // NIMA UCHUN EF FLUENT API EMAS: bular IFODA indekslari
            // (`lower("FullName")`). EF `HasIndex` faqat USTUN ustida indeks
            // qura oladi, ifoda ustida emas — shuning uchun xom SQL.
            //
            // MUHIM: `UserService.ApplySearch` dagi so'rov shakli shu ifodalarga
            // AYNAN mos bo'lishi kerak (`lower("FullName") LIKE ...`), aks holda
            // planner indeksni tanlamaydi.
            //
            // `Email` allaqachon kichik harflarda saqlanadi (Auth/UserService
            // yozishdan oldin `ToLowerInvariant()` qiladi), shuning uchun unga
            // `lower()` kerak emas. `PhoneNormalized` — faqat '+' va raqamlar.
            //
            // PROD ESLATMASI: katta jadvalda `CREATE INDEX CONCURRENTLY` afzal,
            // lekin u tranzaksiya ichida ishlamaydi (migratsiya esa tranzaksiyada
            // bajariladi). Jadval kattalashsa indeksni alohida oynada qurish kerak.
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Users_FullName_Trgm"
                    ON "Users" USING gin (lower("FullName") gin_trgm_ops);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Users_Email_Trgm"
                    ON "Users" USING gin ("Email" gin_trgm_ops);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Users_PhoneNormalized_Trgm"
                    ON "Users" USING gin ("PhoneNormalized" gin_trgm_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Qo'lda qo'shilgan indekslarni EF bilmaydi — qo'lda olib tashlanadi.
            // Kengaytmaning O'ZI (`pg_trgm`) ATAYLAB o'chirilmaydi: uni boshqa
            // migratsiyalar ham ishlatishi mumkin va `DROP EXTENSION` ularning
            // indekslarini ham yo'q qilardi.
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Users_PhoneNormalized_Trgm";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Users_Email_Trgm";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Users_FullName_Trgm";""");

            migrationBuilder.DropIndex(
                name: "IX_Users_PhoneNormalized",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PhoneNormalized",
                table: "Users");

            migrationBuilder.AlterColumn<string>(
                name: "Phone",
                table: "Users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Phone",
                table: "Users",
                column: "Phone",
                unique: true,
                filter: "\"Phone\" IS NOT NULL");
        }
    }
}
