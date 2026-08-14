using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Chat tarixini avtomatik tozalash uchun vaqt indeksi
    /// (<c>ChatRetentionJob</c>). Sabab va rad etilgan muqobil — vazifa
    /// izohida; indeks ustunlarining tartibi —
    /// <c>GroupChatMessageConfiguration</c> da.
    ///
    /// 🔴 ISHLAB CHIQARISHDA QO'LLASHDAN OLDIN O'QING:
    /// <c>CREATE INDEX</c> (CONCURRENTLY'siz) jadvalga YOZISHNI indeks
    /// qurilguncha bloklaydi. <c>GroupChatMessages</c> — eng katta jadval,
    /// ya'ni katta bazada bu bir necha daqiqa davom etishi va shu vaqtda
    /// chatga xabar yozib bo'lmasligi mumkin.
    ///
    /// Bu ATAYLAB shunday qoldirilgan: <c>CREATE INDEX CONCURRENTLY</c>
    /// tranzaksiya ichida ishlamaydi, ya'ni migratsiyani tranzaksiyasiz
    /// bajarishga to'g'ri kelardi — va u YIQILSA orqada YAROQSIZ (invalid)
    /// indeks qolardi, keyingi urinish esa uni "bor" deb o'tkazib yuborardi.
    /// Jimgina yaroqsiz indeks — qisqa to'xtashdan battar.
    ///
    /// Katta bazada uzilishni umuman xohlamasangiz, migratsiyadan OLDIN
    /// indeksni qo'lda quring, keyin migratsiya uni "bor" deb o'tkazib
    /// yuboradi emas — EF baribir yaratishga urinadi, shuning uchun
    /// to'g'ri yo'l: indeksni qo'lda <c>CONCURRENTLY</c> bilan qurib,
    /// so'ng shu migratsiyani <c>__EFMigrationsHistory</c> ga qo'lda
    /// yozib qo'yish (`docs/MIGRATIONS.md` dagi ishlab chiqarish tartibi).
    /// </summary>
    public partial class AddGroupChatMessageSentAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_GroupChatMessages_SentAt",
                table: "GroupChatMessages",
                columns: new[] { "SentAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GroupChatMessages_SentAt",
                table: "GroupChatMessages");
        }
    }
}
