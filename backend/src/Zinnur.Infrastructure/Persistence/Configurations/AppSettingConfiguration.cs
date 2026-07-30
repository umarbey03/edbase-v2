using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Zinnur.Infrastructure.Persistence.Configurations;

/// <summary>
/// Ish jarayonida o'zgartiriladigan sozlamalar jadvali.
/// Izoh va sabab <see cref="AppSetting"/> da.
/// </summary>
public sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    private const int KeyMaxLength = 100;
    private const int ValueMaxLength = 500;

    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("AppSettings");

        // KALIT — birlamchi kalit. Surrogat `Id` QO'YILMAYDI: shunda bitta
        // kalit uchun ikkita qator paydo bo'lishi FIZIK jihatdan imkonsiz va
        // "qaysi qator haqiqiy" degan savol umuman tug'ilmaydi.
        builder.HasKey(s => s.Key);

        builder.Property(s => s.Key).HasMaxLength(KeyMaxLength);
        builder.Property(s => s.Value).IsRequired().HasMaxLength(ValueMaxLength);

        // Kim o'zgartirgani — navigatsiyasiz FK, Restrict (izchillik:
        // `User` ga ishora qiluvchi barcha FK'lar Restrict).
        builder.HasOne<Zinnur.Domain.Entities.User>()
            .WithMany()
            .HasForeignKey(s => s.UpdatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
