using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

/// <summary>
/// Ustoz/kurator jarimasi (2026-08-18). Sabab va falsafa
/// <see cref="Penalty"/> izohida.
/// </summary>
public sealed class PenaltyConfiguration : IEntityTypeConfiguration<Penalty>
{
    public void Configure(EntityTypeBuilder<Penalty> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Penalties");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Kind).HasConversion<int>();
        builder.Property(p => p.Status).HasConversion<int>();
        builder.Property(p => p.Reason).IsRequired().HasMaxLength(Penalty.MaxReasonLength);

        // PUL — `numeric(18,2)` (global konvensiya), lekin OSHKORA
        // yoziladi: moliya ustunining turi konfiguratsiyada ko'rinib tursin.
        builder.Property(p => p.Amount).HasPrecision(18, 2);

        // Miqdor kasrli bo'lishi mumkin (masalan 1.5 soat) — shuning
        // uchun `int` emas.
        builder.Property(p => p.Quantity).HasPrecision(18, 2);

        builder.Property(p => p.PeriodStart).HasColumnType("date");

        // Kategoriya O'CHIRILMAYDI (arxivlanadi) — havola shu sababdan
        // xavfsiz. `Restrict` esa tasodifiy o'chirishni bazada to'sadi.
        builder.HasOne(p => p.Category)
            .WithMany()
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Barcha havolalar `Restrict` — jarima MOLIYAVIY iz. Xodim yoki
        // dars o'chirilsa ham yozuv qolishi kerak (`PayrollAdjustment`
        // bilan AYNI mulohaza).
        builder.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Session)
            .WithMany()
            .HasForeignKey(p => p.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.CreatedBy)
            .WithMany()
            .HasForeignKey(p => p.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.ReviewedBy)
            .WithMany()
            .HasForeignKey(p => p.ReviewedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.PayrollAdjustment)
            .WithMany()
            .HasForeignKey(p => p.PayrollAdjustmentId)
            .OnDelete(DeleteBehavior.Restrict);

        // ═════════════════════════════════════════════════════════════
        // ★ UNIKAL: bitta darsga bitta turdagi jarima FAQAT BIR MARTA.
        //
        // Avtomatik aniqlash fon vazifasidan yuriladi va u qayta
        // ishga tushishi mumkin. Koddagi "bormi?" tekshiruvi ikki
        // jarayon orasidagi poygada ishlamaydi — bu indeks yagona
        // ishonchli to'siq (`MessageOutbox.IdempotencyKey` dagi AYNI
        // mulohaza).
        //
        // QISMAN: qo'lda kiritilgan jarimada `SessionId` bo'sh va
        // ular bir xodimga bir necha marta yozilishi MUMKIN.
        // ═════════════════════════════════════════════════════════════
        builder.HasIndex(p => new { p.SessionId, p.Kind })
            .IsUnique()
            .HasFilter(""" "SessionId" IS NOT NULL """)
            .HasDatabaseName("UX_Penalties_SessionId_Kind");

        // Panel so'rovi: davr + holat bo'yicha, yangidan eskiga.
        builder.HasIndex(p => new { p.PeriodStart, p.Status })
            .HasDatabaseName("IX_Penalties_PeriodStart_Status");

        // "Shu xodimning jarimalari" — profil va oylik ekrani uchun.
        builder.HasIndex(p => new { p.UserId, p.OccurredAt })
            .HasDatabaseName("IX_Penalties_UserId_OccurredAt");
    }
}
