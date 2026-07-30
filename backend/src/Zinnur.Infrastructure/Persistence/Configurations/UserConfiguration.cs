using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.FullName).IsRequired().HasMaxLength(200);
        builder.Property(u => u.Email).IsRequired().HasMaxLength(256);

        // BCrypt hash'i doim 60 belgi; 120 zaxira bilan (algoritm almashsa).
        builder.Property(u => u.PasswordHash).IsRequired().HasMaxLength(120);

        // Foydalanuvchi kiritgan ko'rinish (bo'shliq/qavs/defis bo'lishi mumkin).
        builder.Property(u => u.Phone).HasMaxLength(32);

        // Taqqoslash va qidiruv uchun YAGONA ko'rinish: +998901234567.
        // `private set` — EF backing field orqali o'qib-yozadi.
        builder.Property(u => u.PhoneNormalized).HasMaxLength(20);

        // Enum -> int (SPEC 2-bo'lim). Matn sifatida saqlansa nom o'zgarganda
        // bazadagi eski qiymatlar o'qilmay qoladi.
        builder.Property(u => u.Role).HasConversion<int>();

        // DIQQAT: `IsActive`/`TokenVersion` uchun ataylab HasDefaultValue()
        // ISHLATILMAYDI. EF ustunni CLR default (false/0) bo'lsa INSERT'dan
        // tashlab yuboradi va baza defaultini qo'yadi — natijada
        // `IsActive = false` deb yaratilgan foydalanuvchi bazada `true` bo'lib
        // qolardi. C# property initializer bu ishni xavfsiz bajaradi.

        // ============================================================
        // MOLIYA: BLOKLASHDAN ISTISNO — SOYA (shadow) USTUN
        // ============================================================
        //
        // `PaymentBlockPolicy.IsBlocked` `exempt` argumentini talab qiladi
        // (eski tizimdagi `users.payment_exempt`), lekin `User` entity'sida
        // bunday maydon yo'q va Domain qatlami FAZA 4.3 doirasida
        // O'ZGARTIRILMAYDI. Soya ustun ma'lumotni BAZADA saqlaydi va
        // migratsiyaga tushadi; kodda unga `EF.Property<bool>(u, "PaymentExempt")`
        // orqali murojaat qilinadi (`Application/Payments/PaymentFields.cs`).
        //
        // `HasDefaultValue(false)` bu yerda XAVFSIZ (yuqoridagi `IsActive`
        // holatidan farqli): "istisno emas" — ayni CLR default, ya'ni EF
        // ustunni INSERT'dan tashlab yuborsa ham bazadagi qiymat to'g'ri
        // bo'ladi. U MAVJUD qatorlar uchun ham kerak: migratsiya `NOT NULL`
        // ustunni to'ldirishi shart.
        builder.Property<bool>("PaymentExempt")
            .HasDefaultValue(false);

        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasDatabaseName("IX_Users_Email");

        // FILTRLI UNIKAL INDEKS: PhoneNormalized/TelegramId — nullable.
        // Postgres'da unikal indeks bir nechta NULL'ga ruxsat beradi, lekin
        // filtr indeksni ancha kichraytiradi (o'quvchilarning aksarida telegram
        // bog'lanmagan) va niyat kodda ochiq ko'rinadi.
        // Ustun nomlari PascalCase bo'lgani uchun filtrda TIRNOQ shart.
        //
        // DIQQAT: unikallik XOM `Phone` da EMAS, normalizatsiya qilinganida.
        // Aks holda "+998 90 123 45 67" va "998901234567" ikki xil qator bo'lib
        // o'tib ketardi — eski tizimda aynan shu sabab dublikat profillar
        // paydo bo'lgan va ularni topish uchun butun jadval skan qilinardi.
        builder.HasIndex(u => u.PhoneNormalized)
            .IsUnique()
            .HasFilter("\"PhoneNormalized\" IS NOT NULL")
            .HasDatabaseName("IX_Users_PhoneNormalized");

        builder.HasIndex(u => u.TelegramId)
            .IsUnique()
            .HasFilter("\"TelegramId\" IS NOT NULL")
            .HasDatabaseName("IX_Users_TelegramId");
    }
}
