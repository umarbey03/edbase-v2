using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

/// <summary>
/// Kursga arizalar (2026-08-28). Sabab va falsafa
/// <see cref="EnrollmentApplication"/> izohida.
/// </summary>
public sealed class EnrollmentApplicationConfiguration
    : IEntityTypeConfiguration<EnrollmentApplication>
{
    public void Configure(EntityTypeBuilder<EnrollmentApplication> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("EnrollmentApplications");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.FullName)
            .IsRequired()
            .HasMaxLength(EnrollmentApplication.MaxFullNameLength);

        builder.Property(a => a.Phone)
            .IsRequired()
            .HasMaxLength(EnrollmentApplication.MaxPhoneLength);

        builder.Property(a => a.PhoneNormalized)
            .IsRequired()
            .HasMaxLength(EnrollmentApplication.MaxPhoneLength);

        builder.Property(a => a.Course)
            .HasMaxLength(EnrollmentApplication.MaxCourseLength);

        builder.Property(a => a.Note)
            .HasMaxLength(EnrollmentApplication.MaxNoteLength);

        builder.Property(a => a.Comment)
            .HasMaxLength(EnrollmentApplication.MaxCommentLength);

        /*
          ══════════════════════════════════════════════════════════════
          ★ RAQAM BO'YICHA INDEKS — UNIKAL EMAS, VA BU ATAYLAB.

          Bitta odam ikki marta ariza qoldirishi MUMKIN va bu xato emas:
          birinchi arizasi rad etilgan yoki bir yil oldin bo'lgan
          bo'lishi mumkin. Unikal indeks bunday odamni butunlay to'sib
          qo'yardi va u markazga umuman yeta olmasdi.

          Takroriy YUBORISHGA qarshi himoya BOSHQA joyda va u vaqtga
          bog'liq: `EnrollmentApplicationService` Redis'da qisqa oyna
          ushlab turadi (tugmani ikki marta bosish / bot).
          ══════════════════════════════════════════════════════════════
        */
        builder.HasIndex(a => a.PhoneNormalized)
            .HasDatabaseName("IX_EnrollmentApplications_PhoneNormalized");

        /*
          Ro'yxatning ASOSIY so'rovi: "yangi arizalar, eng yangisi
          yuqorida". Ikkala ustun bitta indeksda — aks holda Postgres
          holat bo'yicha filtrlab, keyin butun natijani saralardi.
        */
        builder.HasIndex(a => new { a.Status, a.CreatedAt })
            .HasDatabaseName("IX_EnrollmentApplications_Status_CreatedAt");

        /*
          ★ `Restrict` (`Cascade` EMAS): arizani ishlagan xodim
          o'chirilsa, ariza ham o'chib ketardi — ya'ni markazning
          konversiya tarixi xodimlar bilan birga yo'qolardi.

          `HandledByUserId` NULL bo'lishi mumkin (hali hech kim
          tegmagan), shuning uchun bog'lanish ixtiyoriy.
        */
        builder.HasOne(a => a.HandledBy)
            .WithMany()
            .HasForeignKey(a => a.HandledByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
