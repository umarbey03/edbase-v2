using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

/// <summary>
/// Guruh kategoriyasi (R21b) — o'quv bo'limi tahrirlaydigan LUG'AT jadvali.
/// </summary>
public sealed class GroupCategoryConfiguration : IEntityTypeConfiguration<GroupCategory>
{
    public void Configure(EntityTypeBuilder<GroupCategory> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("GroupCategories");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).IsRequired().HasMaxLength(GroupCategory.MaxNameLength);

        // ★ NOM NOYOB — LUG'AT jadvalining butun ma'nosi shunda.
        //
        // Ikkita "IELTS" qatori bo'lsa filtr bittasini tanlaydi va guruhlarning
        // yarmi natijadan JIMGINA tushib qolardi — foydalanuvchi esa filtrni
        // ishlamayapti deb o'ylardi (ro'yxat bo'sh emas, faqat NOTO'LIQ).
        //
        // 🔴 INDEKS `lower("Name")` USTIDA EMAS, oddiy ustun ustida — bu
        // ATAYLAB va `Users.Email` bilan AYNI naqsh: funksional indeksni EF
        // model differ'i qayta o'qiy olmaydi va `has-pending-model-changes`
        // abadiy "o'zgarish bor" deb qolardi. Registrsiz ("ielts" va "IELTS")
        // takror servisda tutiladi (`GroupCategoryService.EnsureNameFreeAsync`)
        // va 409 bo'lib qaytadi. Ya'ni indeks — OXIRGI himoya (poyga holati),
        // foydalanuvchiga tushunarli xato esa servisdan keladi.
        builder.HasIndex(c => c.Name)
            .IsUnique()
            .HasDatabaseName("UX_GroupCategories_Name");

        // Ro'yxat DOIM shu tartibda o'qiladi (`Position` -> `Id`), va nofaol
        // kategoriyalar tanlagichdan chiqarib tashlanadi — ya'ni ikkala ustun
        // ham HAR so'rovda ishlatiladi. `Courses` dagi indeks bilan AYNI shakl.
        builder.HasIndex(c => new { c.IsActive, c.Position })
            .HasDatabaseName("IX_GroupCategories_IsActive_Position");
    }
}
