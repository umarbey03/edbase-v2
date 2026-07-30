using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class PaymentAuditConfiguration : IEntityTypeConfiguration<PaymentAudit>
{
    public void Configure(EntityTypeBuilder<PaymentAudit> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PaymentAudits");
        builder.HasKey(a => a.Id);

        // `payment`, `balance`, `discount`, `tariff` — qisqa kodlar.
        builder.Property(a => a.Entity).IsRequired().HasMaxLength(CodeMaxLength);

        // `create`, `update`, `allocate`, `reverse`, `waive`.
        builder.Property(a => a.Action).IsRequired().HasMaxLength(CodeMaxLength);

        builder.Property(a => a.Field).HasMaxLength(FieldMaxLength);

        // Eski/yangi qiymat SATR sifatida: audit har xil turdagi maydonlarni
        // (pul, sana, holat) bir ustunda saqlaydi. `numeric` qilib bo'lmaydi —
        // "Due -> Paid" kabi o'zgarishlar ham shu yerga yoziladi.
        builder.Property(a => a.OldValue).HasMaxLength(ValueMaxLength);
        builder.Property(a => a.NewValue).HasMaxLength(ValueMaxLength);

        builder.Property(a => a.Note).HasMaxLength(PaymentConfiguration.NoteMaxLength);

        // `EntityId` — POLIMORF havola (`Payments.Id`, `StudentAccounts.Id`, ...),
        // shuning uchun FK EMAS. Bitta ustunga bir nechta jadvalga FK
        // qo'yib bo'lmaydi; bu yerda bu ataylab qilingan tanlov, chunki audit
        // ko'chirilgan/o'chirilgan obyektdan ham OMON QOLISHI kerak.

        // O'CHIRISH: Restrict — audit izi pul nizosining yagona dalili.
        // Kaskad bo'lsa, "kim bu oyni to'langan qilib qo'ygan?" degan savolga
        // javob beruvchi qator aynan tekshiruv paytida yo'q bo'lardi.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Amalni bajargan xodim — navigatsiyasiz FK, Restrict (izchillik:
        // `User` ga ishora qiluvchi barcha FK'lar Restrict).
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.ActorId)
            .OnDelete(DeleteBehavior.Restrict);

        // AUDIT SO'ROVI: "shu o'quvchi bo'yicha nima bo'lgan, vaqt tartibida"
        // — nizo tekshiruvidagi BIRINCHI so'rov. Jadval tez o'sadi (har pul
        // amali kamida bitta qator), shuning uchun indekssiz to'liq skan
        // vaqt o'tishi bilan sekinlashib borardi.
        builder.HasIndex(a => new { a.StudentId, a.CreatedAt })
            .HasDatabaseName("IX_PaymentAudits_StudentId_CreatedAt");

        // OBYEKT TARIXI: "shu to'lov yozuvi ustida qanday amallar bo'lgan"
        // — to'lov kartochkasidagi "tarix" bo'limi.
        builder.HasIndex(a => new { a.Entity, a.EntityId })
            .HasDatabaseName("IX_PaymentAudits_Entity_EntityId");
    }

    /// <summary>`payment`, `allocate` kabi qisqa kodlar.</summary>
    private const int CodeMaxLength = 32;

    /// <summary>O'zgargan maydon nomi (`amount`, `status`).</summary>
    private const int FieldMaxLength = 64;

    /// <summary>Eski/yangi qiymat satr ko'rinishida.</summary>
    private const int ValueMaxLength = 500;
}
