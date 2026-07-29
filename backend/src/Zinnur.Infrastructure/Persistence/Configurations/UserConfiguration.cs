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

        // +998901234567 ko'rinishida normalizatsiya qilinadi.
        builder.Property(u => u.Phone).HasMaxLength(20);

        // Enum -> int (SPEC 2-bo'lim). Matn sifatida saqlansa nom o'zgarganda
        // bazadagi eski qiymatlar o'qilmay qoladi.
        builder.Property(u => u.Role).HasConversion<int>();

        // DIQQAT: `IsActive`/`TokenVersion` uchun ataylab HasDefaultValue()
        // ISHLATILMAYDI. EF ustunni CLR default (false/0) bo'lsa INSERT'dan
        // tashlab yuboradi va baza defaultini qo'yadi — natijada
        // `IsActive = false` deb yaratilgan foydalanuvchi bazada `true` bo'lib
        // qolardi. C# property initializer bu ishni xavfsiz bajaradi.

        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasDatabaseName("IX_Users_Email");

        // FILTRLI UNIKAL INDEKS: Phone/TelegramId — nullable.
        // Postgres'da unikal indeks bir nechta NULL'ga ruxsat beradi, lekin
        // filtr indeksni ancha kichraytiradi (o'quvchilarning aksarida telegram
        // bog'lanmagan) va niyat kodda ochiq ko'rinadi.
        // Ustun nomlari PascalCase bo'lgani uchun filtrda TIRNOQ shart.
        builder.HasIndex(u => u.Phone)
            .IsUnique()
            .HasFilter("\"Phone\" IS NOT NULL")
            .HasDatabaseName("IX_Users_Phone");

        builder.HasIndex(u => u.TelegramId)
            .IsUnique()
            .HasFilter("\"TelegramId\" IS NOT NULL")
            .HasDatabaseName("IX_Users_TelegramId");
    }
}
