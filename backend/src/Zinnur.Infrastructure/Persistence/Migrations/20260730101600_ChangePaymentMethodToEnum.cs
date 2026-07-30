using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// To'lov usuli erkin satrdan ENUM'ga o'tkaziladi (naqd -> 0, karta -> 1).
    ///
    /// ★ NIMA UCHUN QO'LDA SQL: EF <c>AlterColumn&lt;int&gt;</c> generatsiya qiladi,
    /// lekin Postgres <c>varchar -> integer</c> o'tkazishni O'ZI bajarmaydi —
    /// <c>"column cannot be cast automatically ... USING"</c> bilan yiqiladi.
    /// Shuning uchun <c>USING</c> ifodasi oshkor yozilgan.
    ///
    /// Xaritalash eski tizimdagi yozuvlarni ham hisobga oladi (`cash`, `naqd`,
    /// `card`, `karta` — registr va bo'shliqqa qaramay). Tanilmagan qiymat
    /// <c>NULL</c> bo'ladi: "usul noma'lum" — bu "naqd" deb TAXMIN qilishdan
    /// yaxshiroq, aks holda kassa hisobotiga soxta qator qo'shilardi.
    /// </summary>
    public partial class ChangePaymentMethodToEnum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "Payments", "PaymentTransactions" })
            {
                migrationBuilder.Sql(
                    $"""
                     ALTER TABLE "{table}"
                     ALTER COLUMN "Method" TYPE integer
                     USING (
                         CASE lower(trim("Method"))
                             WHEN 'naqd'  THEN 0
                             WHEN 'cash'  THEN 0
                             WHEN 'karta' THEN 1
                             WHEN 'card'  THEN 1
                             ELSE NULL
                         END
                     );
                     """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "Payments", "PaymentTransactions" })
            {
                migrationBuilder.Sql(
                    $"""
                     ALTER TABLE "{table}"
                     ALTER COLUMN "Method" TYPE character varying(32)
                     USING (
                         CASE "Method"
                             WHEN 0 THEN 'naqd'
                             WHEN 1 THEN 'karta'
                             ELSE NULL
                         END
                     );
                     """);
            }
        }
    }
}
